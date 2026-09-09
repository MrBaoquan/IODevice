namespace IOToolkit.ActTrack
{
    /// <summary>ActTrack 互动埋点设备的输入类型.</summary>
    public enum InputType
    {
        Button = 1, // 按钮
        Handle, // 手柄
        Joystick, // 摇杆
        MotorStart, // 电机启动
        Lever, // 拉杆
        Knob, // 旋钮
        Sensor, // 传感器
        Handwheel, // 手轮
        Camera, // 摄像头
        Radar, // 雷达
        Touch, // 触摸键
        Key, // 按键
        RFID, // RFID
        Turntable, // 转盘
        Slider, // 滑杆
        Kinect // 体感器
    }

    /// <summary>
    /// IOToolkit 数据采集设备 (IOUI-*-ACTTRACK) 的语法糖.
    /// 通道 0 标识互动起止, 其他通道按 InputType 偏移指向具体输入.
    /// </summary>
    public static class ActTrackExtension
    {
        public static void NotifyStart(this IODevice actTrackDevice)
        {
            actTrackDevice.SetDO(IOKeyCode.OAxis_00, 1);
        }

        public static void NotifyEnd(this IODevice actTrackDevice)
        {
            actTrackDevice.SetDO(IOKeyCode.OAxis_00, 0);
        }

        public static void NotifyButton(this IODevice actTrackDevice, int buttonID = 1)
        {
            actTrackDevice.NotifyInput(InputType.Button, buttonID);
        }

        public static void NotifyInput(
            this IODevice actTrackDevice,
            InputType inputType,
            int buttonID = 1
        )
        {
            Key _inputKey = "OAxis_" + ((int)inputType).ToString("00");
            actTrackDevice.SetDO(_inputKey, buttonID);
        }

        public static void NotifyAlarm(this IODevice actTrackDevice, int alarmID)
        {
            actTrackDevice.SetDO(IOKeyCode.OAxis_250, alarmID);
        }
    }
}
