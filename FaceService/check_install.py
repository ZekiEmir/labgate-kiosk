import importlib
import sys

failed = False
print("Python:", sys.version.split()[0])
for name in ("numpy", "cv2", "dlib", "face_recognition"):
    try:
        module = importlib.import_module(name)
        print("OK:", name, getattr(module, "__version__", ""))
    except Exception as error:
        print("ERROR:", name, str(error))
        failed = True
sys.exit(1 if failed else 0)
