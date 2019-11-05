using IOToolkit.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static IOToolkit.Core.IONativeWrapper;

namespace IOToolkit
{
    public abstract partial class IODevice
    {
        public IODevice(string InID)
        {
            this.ID = InID;
        }

        public bool IsValid()
        {
            return true;
        }
        
        public void BindKey(Key InKey, InputEvent InEvent, Action InHandler)
        {   
            NativeActionSignature _proxy = new NativeActionSignature(() =>
            {
                InHandler();
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindKey(this.ID, InKey,(int)InEvent, _proxy);
        }

        public void BindAction(string InActionName,InputEvent InEvent, Action InHandler)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler();
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent, _proxy);
        }
        public void BindAction(string InActionName,InputEvent InEvent, Action<Key> InHandler)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(InKey);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent,_proxy);
        }

        public void BindAction<T1>(string InActionName, InputEvent InEvent, Action<T1> InHandler, T1 _param1)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(_param1);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent, _proxy);
        }
        public void BindAction<T1>(string InActionName, InputEvent InEvent, Action<Key,T1> InHandler, T1 _param1)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(InKey, _param1);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent, _proxy);
        }

        public void BindAction<T1,T2>(string InActionName, InputEvent InEvent, Action<T1,T2> InHandler, T1 _param1, T2 _param2)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(_param1, _param2);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent,_proxy);
        }
        public void BindAction<T1,T2>(string InActionName, InputEvent InEvent, Action<Key, T1, T2> InHandler, T1 _param1, T2 _param2)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(InKey, _param1, _param2);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent, _proxy);
        }

        public void BindAction<T1, T2, T3>(string InActionName, InputEvent InEvent, Action<T1, T2, T3> InHandler, T1 _param1, T2 _param2, T3 _param3)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(_param1, _param2, _param3);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent, _proxy);
        }
        public void BindAction<T1, T2, T3>(string InActionName, InputEvent InEvent, Action<Key, T1, T2, T3> InHandler, T1 _param1, T2 _param2, T3 _param3)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(InKey, _param1, _param2, _param3);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent, _proxy);
        }

        /**
         * 
         * Bind Axis
         * 
         */
        public void BindAxis(string InAxisName, Action<float> InHandler)
        {
            NativeAxisSignature _proxy = new NativeAxisSignature((float InVal) =>
            {
                InHandler(InVal);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAxis(this.ID, InAxisName, _proxy);
        }

        public byte GetDeviceDO(Key InKey)
        {
            return IONativeWrapper.GetDOSingle(this.ID, InKey);
        }

        public int GetDeviceDO(byte[] DOStatus)
        {
            return IONativeWrapper.GetDOAll(this.ID, DOStatus);
        }

        public int SetDeviceDO(Key InKey, byte InStatus)
        {
            return IONativeWrapper.SetDOSingle(this.ID, InKey, InStatus);
        }

        public int SetDeviceDO(byte[] InStatus)
        {
            return IONativeWrapper.SetDOAll(this.ID, InStatus);
        }


        public bool GetKey(string InKey)
        {
            return IONativeWrapper.GetKey(this.ID, InKey);
        }

        public bool GetKeyDown(Key InKey)
        {
            return IONativeWrapper.GetKeyDown(this.ID, InKey);
        }

        public bool GetKeyUp(Key InKey)
        {
            return IONativeWrapper.GetKeyUp(this.ID, InKey);
        }

        public float GetAxis(string InAxisName)
        {
            return IONativeWrapper.GetAxis(this.ID, InAxisName);
        }

        public float GetAxisKey(Key InKey)
        {
            return IONativeWrapper.GetAxisKey(this.ID, InKey);
        }

        public float GetKeyDownDuration(Key InKey)
        {
            return IONativeWrapper.GetKeyDownDuration(this.ID, InKey);
        }

        //  ------------- Properties -------------
        public string ID;

        // 引用回调函数,防止被GC
        private List<Delegate> delegateRefs = new List<Delegate>();

    }
}
