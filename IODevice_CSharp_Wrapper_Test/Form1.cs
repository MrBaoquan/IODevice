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
            IODeviceController.UnLoad();
            IODeviceController.Load();

            IODevice _device = IODeviceController.GetIODevice("Standard");
            _device.BindAction("TestAction", InputEvent.IE_Pressed, this.onActionPressed);
            _device.BindAction("TestAction", InputEvent.IE_Released, this.onActionReleased);


            _device.BindKey(IOKeyCode.AnyKey, InputEvent.IE_Pressed, this.onAnyKeyPressed);
            _device.BindKey(IOKeyCode.AnyKey, InputEvent.IE_Released, this.onAnyKeyReleased);

            _device.BindAxis("MoveLR", this.OnMove);
            timer1.Start();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            IODeviceController.Update();

            IODevice _device = IODeviceController.GetIODevice("Standard");
            byte _test = _device.GetDeviceDO(IOKeyCode.A);
            byte[] _status = new byte[32];
            _status[1] = 1;
            _status[31] = 1;
            _device.SetDeviceDO(_status);
            _device.SetDeviceDO(IOKeyCode.A, 1);
            if (_device.GetKeyDown(IOKeyCode.A))
            {
                Debug.WriteLine("A Pressed");
            }
            if (_device.GetKeyUp(IOKeyCode.A))
            {
                Debug.WriteLine("A Released");
            }
            //Debug.WriteLine(_device.GetKeyDownDuration(IOKeyCode.B));
            //Debug.WriteLine(_device.GetAxisKey(IOKeyCode.MouseX));
            Debug.WriteLine(_device.GetAxis("MoveLR"));
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
