"""One request per process. JSON stdin/stdout; captures server webcam, never uploaded images."""
import base64
import contextlib
import json
import math
import sys


def valid_encoding(values):
    return (isinstance(values, list) and len(values) == 128
            and all(type(v) in (int, float) and math.isfinite(v) and abs(v) <= 10 for v in values))


def run(operation, request, cv2, recognition, np):
    if operation not in ("snapshot", "enroll", "verify"):
        return {"ok": False, "error": "invalid_request"}
    camera_index = request.get("cameraIndex", 0)
    tolerance = request.get("tolerance", 0.5)
    if (type(camera_index) is not int or not 0 <= camera_index <= 10
            or type(tolerance) not in (float, int) or not math.isfinite(tolerance)
            or not 0 < tolerance <= 0.6):
        return {"ok": False, "error": "invalid_request"}
    if operation == "verify" and not valid_encoding(request.get("encoding")):
        return {"ok": False, "error": "invalid_encoding"}

    backend = cv2.CAP_DSHOW if sys.platform == "win32" else cv2.CAP_ANY
    camera = cv2.VideoCapture(camera_index, backend)
    try:
        if not camera.isOpened():
            return {"ok": False, "error": "camera_unavailable"}
        camera.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
        camera.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
        frame = None
        # Discard initial frames to let exposure settle. Parent enforces a 30s deadline.
        for _ in range(6):
            ok, image = camera.read()
            if ok and image is not None:
                frame = image
        if frame is None:
            return {"ok": False, "error": "capture_failed"}
    finally:
        camera.release()

    height, width = frame.shape[:2]
    if width > 640:
        frame = cv2.resize(frame, (640, max(1, round(height * 640 / width))))
    if operation == "snapshot":
        ok, image = cv2.imencode(".jpg", frame, [cv2.IMWRITE_JPEG_QUALITY, 80])
        if not ok:
            return {"ok": False, "error": "capture_failed"}
        return {"ok": True, "image": base64.b64encode(image.tobytes()).decode("ascii")}

    rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
    locations = recognition.face_locations(rgb, model="hog")
    if not locations:
        return {"ok": False, "error": "no_face"}
    if len(locations) != 1:
        return {"ok": False, "error": "multiple_faces"}
    vectors = recognition.face_encodings(rgb, known_face_locations=locations, num_jitters=1, model="small")
    if len(vectors) != 1 or not valid_encoding(vectors[0].tolist()):
        return {"ok": False, "error": "invalid_encoding"}
    if operation == "enroll":
        return {"ok": True, "encoding": vectors[0].tolist()}
    reference = np.asarray(request["encoding"], dtype=np.float64)
    distance = float(recognition.face_distance([reference], vectors[0])[0])
    if not math.isfinite(distance) or distance < 0:
        return {"ok": False, "error": "invalid_distance"}
    return {"ok": True, "match": distance <= tolerance, "distance": distance}


def main():
    operation = sys.argv[1] if len(sys.argv) == 2 else "invalid"
    try:
        request = json.loads(sys.stdin.read(20_000))
        if not isinstance(request, dict):
            raise ValueError("Object expected")
    except (ValueError, TypeError):
        return {"ok": False, "error": "invalid_request"}
    try:
        # Some third party modules print messages during import; keep stdout strict JSON.
        with contextlib.redirect_stdout(sys.stderr):
            import cv2
            if operation == "snapshot":
                recognition = None
                np = None
            else:
                import face_recognition as recognition
                import numpy as np
            return run(operation, request, cv2, recognition, np)
    except ImportError:
        return {"ok": False, "error": "dependencies_missing"}
    except Exception:
        return {"ok": False, "error": "worker_failed"}


if __name__ == "__main__":
    print(json.dumps(main(), allow_nan=False))
