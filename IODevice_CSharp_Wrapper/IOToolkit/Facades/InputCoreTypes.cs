using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOToolkit
{
    public enum InputEvent
    {
        IE_Pressed = 0,
        IE_Released = 1,
        IE_Repeat = 2,
        IE_DoubleClick = 3,
        IE_Axis = 4,
        IE_MAX = 5
    }

    public class Key
    {
        Key(string InID = "string InID")
        {
            this.ID = InID;
        }
        // ------------- Properties -----------
        string ID;

        public override string ToString()
        {
            return this.ID;
        }

        public static implicit operator Key(string v)
        {
            return new Key(v);
        }

        public static implicit operator string(Key v)
        {
            return v.ID;
        }
    }

    public struct IOKeyCode
    {
        /** Standard Keyboard & Mouse */
        public static readonly Key AnyKey = "AnyKey";
        public static readonly Key MouseX = "MouseX";
        public static readonly Key MouseY = "MouseY";
        public static readonly Key MouseScrollUp = "MouseScrollUp";
        public static readonly Key MouseScrollDown = "MouseScrollDown";
        public static readonly Key MouseWheelAxis = "MouseWheelAxis";

        public static readonly Key LeftMouseButton = "LeftMouseButton";
        public static readonly Key RightMouseButton = "RightMouseButton";
        public static readonly Key MiddleMouseButton = "MiddleMouseButton";
        public static readonly Key ThumbMouseButton = "ThumbMouseButton";
        public static readonly Key ThumbMouseButton2 = "ThumbMouseButton2";

        public static readonly Key BackSpace = "BackSpace";
        public static readonly Key Tab = "Tab";
        public static readonly Key Enter = "Enter";
        public static readonly Key Pause = "Pause";

        public static readonly Key CapsLock = "CapsLock";
        public static readonly Key Escape = "Escape";
        public static readonly Key SpaceBar = "SpaceBar";
        public static readonly Key PageUp = "PageUp";
        public static readonly Key PageDown = "PageDown";
        public static readonly Key End = "End";
        public static readonly Key Home = "Home";

        public static readonly Key Left = "Left";
        public static readonly Key Up = "Up";
        public static readonly Key Right = "Right";
        public static readonly Key Down = "Down";

        public static readonly Key Insert = "Insert";
        public static readonly Key Delete = "Delete";

        public static readonly Key Zero = "Zero";
        public static readonly Key One = "One";
        public static readonly Key Two = "Two";
        public static readonly Key Three = "Three";
        public static readonly Key Four = "Four";
        public static readonly Key Five = "Five";
        public static readonly Key Six = "Six";
        public static readonly Key Seven = "Seven";
        public static readonly Key Eight = "Eight";
        public static readonly Key Nine = "Nine";

        public static readonly Key A = "A";
        public static readonly Key B = "B";
        public static readonly Key C = "C";
        public static readonly Key D = "D";
        public static readonly Key E = "E";
        public static readonly Key F = "F";
        public static readonly Key G = "G";
        public static readonly Key H = "H";
        public static readonly Key I = "I";
        public static readonly Key J = "J";
        public static readonly Key K = "K";
        public static readonly Key L = "L";
        public static readonly Key M = "M";
        public static readonly Key N = "N";
        public static readonly Key O = "O";
        public static readonly Key P = "P";
        public static readonly Key Q = "Q";
        public static readonly Key R = "R";
        public static readonly Key S = "S";
        public static readonly Key T = "T";
        public static readonly Key U = "U";
        public static readonly Key V = "V";
        public static readonly Key W = "W";
        public static readonly Key X = "X";
        public static readonly Key Y = "Y";
        public static readonly Key Z = "Z";

        public static readonly Key NumPadZero = "NumPadZero";
        public static readonly Key NumPadOne = "NumPadOne";
        public static readonly Key NumPadTwo = "NumPadTwo";
        public static readonly Key NumPadThree = "NumPadThree";
        public static readonly Key NumPadFour = "NumPadFour";
        public static readonly Key NumPadFive = "NumPadFive";
        public static readonly Key NumPadSix = "NumPadSix";
        public static readonly Key NumPadSeven = "NumPadSeven";
        public static readonly Key NumPadEight = "NumPadEight";
        public static readonly Key NumPadNine = "NumPadNine";

        public static readonly Key Multiply = "Multiply";
        public static readonly Key Add = "Add";
        public static readonly Key Subtract = "Subtract";
        public static readonly Key Decimal = "Decimal";
        public static readonly Key Divide = "Divide";

        public static readonly Key F1 = "F1";
        public static readonly Key F2 = "F2";
        public static readonly Key F3 = "F3";
        public static readonly Key F4 = "F4";
        public static readonly Key F5 = "F5";
        public static readonly Key F6 = "F6";
        public static readonly Key F7 = "F7";
        public static readonly Key F8 = "F8";
        public static readonly Key F9 = "F9";
        public static readonly Key F10 = "F10";
        public static readonly Key F11 = "F11";
        public static readonly Key F12 = "F12";

        public static readonly Key NumLock = "NumLock";

        public static readonly Key ScrollLock = "ScrollLock";

        public static readonly Key LeftShift = "LeftShift";
        public static readonly Key RightShift = "RightShift";
        public static readonly Key LeftControl = "LeftControl";
        public static readonly Key RightControl = "RightControl";
        public static readonly Key LeftAlt = "LeftAlt";
        public static readonly Key RightAlt = "RightAlt";
        public static readonly Key LeftCommand = "LeftCommand";
        public static readonly Key RightCommand = "RightCommand";
        public static readonly Key Invalid = "Invalid";

        /** Buttons for PCI & JoyStick  */
        public static readonly Key Button_00 = "Button_00";
        public static readonly Key Button_01 = "Button_01";
        public static readonly Key Button_02 = "Button_02";
        public static readonly Key Button_03 = "Button_03";
        public static readonly Key Button_04 = "Button_04";
        public static readonly Key Button_05 = "Button_05";
        public static readonly Key Button_06 = "Button_06";
        public static readonly Key Button_07 = "Button_07";
        public static readonly Key Button_08 = "Button_08";
        public static readonly Key Button_09 = "Button_09";
        public static readonly Key Button_10 = "Button_10";
        public static readonly Key Button_11 = "Button_11";
        public static readonly Key Button_12 = "Button_12";
        public static readonly Key Button_13 = "Button_13";
        public static readonly Key Button_14 = "Button_14";
        public static readonly Key Button_15 = "Button_15";
        public static readonly Key Button_16 = "Button_16";
        public static readonly Key Button_17 = "Button_17";
        public static readonly Key Button_18 = "Button_18";
        public static readonly Key Button_19 = "Button_19";
        public static readonly Key Button_20 = "Button_20";
        public static readonly Key Button_21 = "Button_21";
        public static readonly Key Button_22 = "Button_22";
        public static readonly Key Button_23 = "Button_23";
        public static readonly Key Button_24 = "Button_24";
        public static readonly Key Button_25 = "Button_25";
        public static readonly Key Button_26 = "Button_26";
        public static readonly Key Button_27 = "Button_27";
        public static readonly Key Button_28 = "Button_28";
        public static readonly Key Button_29 = "Button_29";
        public static readonly Key Button_30 = "Button_30";
        public static readonly Key Button_31 = "Button_31";


        public static readonly Key Axis_00 = "Axis_00";
        public static readonly Key Axis_01 = "Axis_01";
        public static readonly Key Axis_02 = "Axis_02";
        public static readonly Key Axis_03 = "Axis_03";
        public static readonly Key Axis_04 = "Axis_04";
        public static readonly Key Axis_05 = "Axis_05";
        public static readonly Key Axis_06 = "Axis_06";
        public static readonly Key Axis_07 = "Axis_07";
        public static readonly Key Axis_08 = "Axis_08";
        public static readonly Key Axis_09 = "Axis_09";
        public static readonly Key Axis_10 = "Axis_10";
        public static readonly Key Axis_11 = "Axis_11";
        public static readonly Key Axis_12 = "Axis_12";
        public static readonly Key Axis_13 = "Axis_13";
        public static readonly Key Axis_14 = "Axis_14";
        public static readonly Key Axis_15 = "Axis_15";
        public static readonly Key Axis_16 = "Axis_16";
        public static readonly Key Axis_17 = "Axis_17";
        public static readonly Key Axis_18 = "Axis_18";
        public static readonly Key Axis_19 = "Axis_19";
        public static readonly Key Axis_20 = "Axis_20";
        public static readonly Key Axis_21 = "Axis_21";
        public static readonly Key Axis_22 = "Axis_22";
        public static readonly Key Axis_23 = "Axis_23";
        public static readonly Key Axis_24 = "Axis_24";
        public static readonly Key Axis_25 = "Axis_25";
        public static readonly Key Axis_26 = "Axis_26";
        public static readonly Key Axis_27 = "Axis_27";
        public static readonly Key Axis_28 = "Axis_28";
        public static readonly Key Axis_29 = "Axis_29";
        public static readonly Key Axis_30 = "Axis_30";
        public static readonly Key Axis_31 = "Axis_31";

        /** Joystick axes */
         /** Joystick axes */
        public static readonly Key JS_X = "JS_X";                     /* x-axis position              */
        public static readonly Key JS_Y = "JS_Y";                     /* y-axis position              */
        public static readonly Key JS_Z = "JS_Z";                     /* z-axis position              */
        public static readonly Key JS_Rx = "JS_Rx";                    /* x-axis rotation              */
        public static readonly Key JS_Ry = "JS_Ry";                    /* y-axis rotation              */
        public static readonly Key JS_Rz = "JS_Rz";                    /* z-axis rotation              */
        public static readonly Key JS_VX = "JS_VX";                    /* x-axis velocity              */
        public static readonly Key JS_VY = "JS_VY";                    /* y-axis velocity              */
        public static readonly Key JS_VZ = "JS_VZ";                    /* z-axis velocity              */
        public static readonly Key JS_VRx = "JS_VRx";                   /* x-axis angular velocity      */
        public static readonly Key JS_VRy = "JS_VRy";                   /* y-axis angular velocity      */
        public static readonly Key JS_VRz = "JS_VRz";                   /* z-axis angular velocity      */
        public static readonly Key JS_AX = "JS_AX";                    /* x-axis acceleration          */
        public static readonly Key JS_AY = "JS_AY";                    /* y-axis acceleration          */
        public static readonly Key JS_AZ = "JS_AZ";                    /* z-axis acceleration          */
        public static readonly Key JS_ARx = "JS_ARx";                   /* x-axis angular acceleration  */
        public static readonly Key JS_ARy = "JS_ARy";                   /* y-axis angular acceleration  */
        public static readonly Key JS_ARz = "JS_ARz";                   /* z-axis angular acceleration  */
        public static readonly Key JS_FX = "JS_FX";                    /* x-axis force                 */
        public static readonly Key JS_FY = "JS_FY";                    /* y-axis force                 */
        public static readonly Key JS_FZ = "JS_FZ";                    /* z-axis force                 */
        public static readonly Key JS_FRx = "JS_FRx";                   /* x-axis torque                */
        public static readonly Key JS_FRy = "JS_FRy";                   /* y-axis torque                */
        public static readonly Key JS_FRz = "JS_FRz";                   /* z-axis torque                */

        public static readonly Key JS_Slider_00 = "JS_Slider_00";           /* extra axes positions         */
        public static readonly Key JS_Slider_01 = "JS_Slider_01";           /* extra axes positions         */

        public static readonly Key JS_VSlider_00 = "JS_VSlider_00";          /* extra axes velocities        */
        public static readonly Key JS_VSlider_01 = "JS_VSlider_01";          /* extra axes velocities        */

        public static readonly Key JS_ASlider_00 = "JS_ASlider_00";          /* extra axes accelerations     */
        public static readonly Key JS_ASlider_01 = "JS_ASlider_01";          /* extra axes accelerations     */

        public static readonly Key JS_FSlider_00 = "JS_FSlider_00";          /* extra axes forces            */
        public static readonly Key JS_FSlider_01 = "JS_FSlider_01";          /* extra axes forces            */

        public static readonly Key JS_POV_00 = "JS_POV_00";             /* POV directions               */
        public static readonly Key JS_POV_01 = "JS_POV_01";             /* POV directions               */
        public static readonly Key JS_POV_02 = "JS_POV_02";             /* POV directions               */
        public static readonly Key JS_POV_03 = "JS_POV_03";             /* POV directions               */
    };
}
