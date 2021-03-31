using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using IOToolkit.Core;
using IOToolkit;
using System.Diagnostics;

namespace IODevice_C_CSharpTest
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        //NativeDelegate.NativeActionSignature _delegate;

        private void Form1_Load(object sender, EventArgs e)
        {
            //_delegate = new NativeDelegate.NativeActionSignature(this.onKeyPressed);
            IODeviceController.Load();

            IODevice _device = IODeviceController.GetIODevice("ExtDev");
            _device.BindAction("TestAction", InputEvent.IE_Pressed, this.onActionPressed);
            _device.BindAction("TestAction", InputEvent.IE_Released, this.onActionReleased);

            //_device.BindKey(IOKeyCode.AnyKey, InputEvent.IE_Pressed, this.onAnyKeyPressed);
            //_device.BindKey(IOKeyCode.AnyKey, InputEvent.IE_Released, this.onAnyKeyReleased);

            _device.BindAxis("MoveLR", this.OnMove);
            timer1.Start();
            timer1.Interval = 20;
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            IODeviceController.Update();

            IODevice _device = IODeviceController.GetIODevice("ExtDev");
            var _ret = _device.GetKeyDown(IOKeyCode.A);
            if (_ret)
            {
                int _res = _device.SetDO("Start",1.0f);
                Debug.WriteLine(_device.GetDO("Start"));
            }
            if (_device.GetKeyUp(IOKeyCode.A))
            {
                //_device.SetDO("Start",0.0f);
                Debug.WriteLine(_device.GetDO("Start"));
            }

            //Debug.WriteLine(_device.GetDO("Start"));
            //Debug.WriteLine(_device.GetKeyDownDuration(IOKeyCode.B));
            //Debug.WriteLine(_device.GetAxisKey(IOKeyCode.MouseX));
            Debug.WriteLine(_device.GetAxis("TestAxis"));
        }

        private void onAnyKeyPressed()
        {
            keyLabel.Text = "AnyKey Pressed";
        }

        private void onAnyKeyReleased()
        {
            keyLabel.Text = "AnyKey Released";
        }

        private void onAnyKeyReleased(Key InKey)
        {
            keyLabel.Text = InKey + " Released";
        }

        private void onActionPressed(Key InKeyName)
        {
            actionLabel.Text = InKeyName + "Pressed";
            Debug.WriteLine(InKeyName);
        }

        private void onActionReleased(Key InKey)
        {
            actionLabel.Text = InKey + "Released";
        }

        private void OnMove(float InVal)
        {
            GC.Collect();
            axisLabel.Text = InVal.ToString();
        }
    }
}
