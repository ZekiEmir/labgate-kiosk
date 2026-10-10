import math
import unittest
from unittest.mock import Mock
import numpy as np
from face_worker import run, valid_encoding


class WorkerTests(unittest.TestCase):
    def setUp(self):
        self.cv = Mock()
        self.camera = self.cv.VideoCapture.return_value
        self.camera.isOpened.return_value = True
        self.camera.read.return_value = (True, np.zeros((480, 640, 3), dtype=np.uint8))
        self.recognition = Mock()
        self.recognition.face_locations.return_value = [(0, 100, 100, 0)]
        self.recognition.face_encodings.return_value = [np.zeros(128)]
        self.recognition.face_distance.side_effect = lambda refs, vector: [np.linalg.norm(refs[0] - vector)]

    def execute(self, operation="verify", **values):
        request = {"cameraIndex": 0, "encoding": [0.0] * 128, "tolerance": 0.5, **values}
        return run(operation, request, self.cv, self.recognition, np)

    def test_reference_vector(self):
        result = self.execute("enroll")
        self.assertTrue(result["ok"])
        self.assertEqual(len(result["encoding"]), 128)
        self.camera.release.assert_called_once()

    def test_match(self):
        self.assertTrue(self.execute()["match"])

    def test_mismatch(self):
        self.assertFalse(self.execute(encoding=[0.1] * 128)["match"])

    def test_threshold_boundary(self):
        self.assertTrue(self.execute(encoding=[0.5] + [0.0] * 127)["match"])
        self.assertFalse(self.execute(encoding=[0.5001] + [0.0] * 127)["match"])

    def test_no_face(self):
        self.recognition.face_locations.return_value = []
        self.assertEqual(self.execute()["error"], "no_face")

    def test_multiple_faces(self):
        self.recognition.face_locations.return_value = [(0, 1, 1, 0)] * 2
        self.assertEqual(self.execute()["error"], "multiple_faces")

    def test_camera_unavailable(self):
        self.camera.isOpened.return_value = False
        self.assertEqual(self.execute()["error"], "camera_unavailable")
        self.camera.release.assert_called_once()

    def test_capture_failed(self):
        self.camera.read.return_value = (False, None)
        self.assertEqual(self.execute()["error"], "capture_failed")
        self.camera.release.assert_called_once()

    def test_capture_exception_releases(self):
        self.camera.read.side_effect = OSError("unplugged")
        with self.assertRaises(OSError):
            self.execute()
        self.camera.release.assert_called_once()

    def test_invalid_reference(self):
        for value in ([0] * 127, [math.nan] * 128, [math.inf] * 128, [True] * 128):
            self.assertFalse(valid_encoding(value))
            self.assertEqual(self.execute(encoding=value)["error"], "invalid_encoding")
        self.cv.VideoCapture.assert_not_called()

    def test_bad_config(self):
        self.assertEqual(self.execute(tolerance=0.9)["error"], "invalid_request")
        self.assertEqual(self.execute(cameraIndex=-1)["error"], "invalid_request")
        self.cv.VideoCapture.assert_not_called()

    def test_invalid_distance(self):
        self.recognition.face_distance.side_effect = None
        self.recognition.face_distance.return_value = [math.nan]
        self.assertEqual(self.execute()["error"], "invalid_distance")


if __name__ == "__main__":
    unittest.main()
