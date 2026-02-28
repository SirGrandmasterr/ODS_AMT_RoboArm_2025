## Visual Tracking

**IMPORTANT**: All paths regarding Visual Tracking will be relative to the folder `Assets/SceneEnvironments/NonVRScenes/TrainingScenes/tracking/`

1. Open scene `RobotArmRefactored_IK_TR`
2. Ensure the following settings (should be set already)
   1. `TrainingEnvironment` > `VisualTracking`
      1. Enabled = true
      2. Cameras = <list_of_the_3_child_cameras>
      3. ModelAsset = yolov8x.onnx
      4. Normalize Input to 255 = false
      5. Bottle = Bottle
      6. Use Static Image = false
      7. Test Image = None
      8. Visualize = true
      9. Debug View = <list_of_the_3_Raw_Images_of_Debug_View>
   2.  `TrainingEnvironment` > `RoboArmConstruct` > `RobotArm` > ARM Agent_IK_TR (Script)
       1.  Enable Visual Tracking = true
   3.  `Spawner`: Total Environment Count = <Instance_of_Training_Environment_Spawned_On_Play> (1 for testing) 

### Visual Tracking Object
- Has the Cameras as children, that are used for tracking
- Contains references to the Raw Images of Debug View

### Debug View Object
- Enable `DebugView` Object to visualize camera images during play mode

### Tracking Model
- `yolov8x.onnx` should be found in the folder `Models/Tracking`
- In case you want to use another YOLO model
  - `pip install ultralytics`
  - `yolo export model=yolov8x.pt format=onnx opset=12`
  - Set it at `TrainingEnvironment` > `VisualTracking` > `ModelAsset`


### Training
- Best set `Spawner` > Total Environment Count = A value larger than 1

### Play Mode
Either
- Enable `DebugView` Object to visualize camera images OR
- Use W, A, S, and D to walk to the conveyor belt