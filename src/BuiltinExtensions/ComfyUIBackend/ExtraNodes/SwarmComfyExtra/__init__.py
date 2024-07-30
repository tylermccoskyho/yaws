NODE_CLASS_MAPPINGS = {}

# RemBg doesn't work on all python versions and OS's
try:
    from . import SwarmRemBg
    NODE_CLASS_MAPPINGS.update(SwarmRemBg.NODE_CLASS_MAPPINGS)
except ImportError:
    pass
# This uses FFMPEG which doesn't install itself properly on Macs I guess?
try:
    from . import SwarmSaveAnimationWS
    NODE_CLASS_MAPPINGS.update(SwarmSaveAnimationWS.NODE_CLASS_MAPPINGS)
except ImportError:
    pass
# Yolo uses Ultralytics, which is cursed
try:
    from . import SwarmYolo
    NODE_CLASS_MAPPINGS.update(SwarmYolo.NODE_CLASS_MAPPINGS)
except ImportError:
    pass
