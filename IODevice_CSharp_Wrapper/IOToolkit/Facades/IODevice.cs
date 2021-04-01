using IOToolkit.Core;
using System;
using System.Collections.Generic;

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
        
        /// <summary>
        /// 绑定一个Key的事件
        /// </summary>
        /// <param name="InKey">需要绑定的按键</param>
        /// <param name="InEvent">事件类型</param>
        /// <param name="InHandler">事件响应回调</param>
        public void BindKey(Key InKey, InputEvent InEvent, Action InHandler)
        {   
            NativeActionSignature _proxy = new NativeActionSignature(() =>
            {
                InHandler();
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindKey(this.ID, InKey,(int)InEvent, _proxy);
        }

        /// <summary>
        /// 绑定Action事件
        /// </summary>
        /// <param name="InActionName">Action 名称</param>
        /// <param name="InEvent">事件类型</param>
        /// <param name="InHandler">事件响应回调</param>

        public void BindAction(string InActionName,InputEvent InEvent, Action InHandler)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler();
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent, _proxy);
        }

        /// <summary>
        /// 绑定Action事件 回调携带Key参数
        /// </summary>
        /// <param name="InActionName">Action名称</param>
        /// <param name="InEvent">事件类型</param>
        /// <param name="InHandler">事件响应回调</param>
        public void BindAction(string InActionName,InputEvent InEvent, Action<Key> InHandler)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(InKey);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent,_proxy);
        }

        /// <summary>
        /// 绑定Action事件 回调携带1个自定义参数
        /// </summary>
        /// <typeparam name="T1">自定义参数类型</typeparam>
        /// <param name="InActionName">Action名称</param>
        /// <param name="InEvent">事件类型</param>
        /// <param name="InHandler">事件响应回调</param>
        /// <param name="_param1">自定义参数</param>

        public void BindAction<T1>(string InActionName, InputEvent InEvent, Action<T1> InHandler, T1 _param1)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(_param1);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent, _proxy);
        }
        /// <summary>
        /// 绑定Action事件 回调携带Key及1个自定义参数
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <param name="InActionName">Action名称</param>
        /// <param name="InEvent">事件类型</param>
        /// <param name="InHandler">事件响应回调</param>
        /// <param name="_param1">自定义参数</param>
        public void BindAction<T1>(string InActionName, InputEvent InEvent, Action<Key,T1> InHandler, T1 _param1)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(InKey, _param1);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent, _proxy);
        }
        /// <summary>
        /// 绑定Action事件, 回调携带2个自定义参数
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <param name="InActionName"></param>
        /// <param name="InEvent"></param>
        /// <param name="InHandler"></param>
        /// <param name="_param1"></param>
        /// <param name="_param2"></param>
        public void BindAction<T1,T2>(string InActionName, InputEvent InEvent, Action<T1,T2> InHandler, T1 _param1, T2 _param2)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(_param1, _param2);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent,_proxy);
        }

        /// <summary>
        /// 绑定Action事件 回调携带Key及2个自定义参数
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <param name="InActionName"></param>
        /// <param name="InEvent"></param>
        /// <param name="InHandler"></param>
        /// <param name="_param1"></param>
        /// <param name="_param2"></param>
        public void BindAction<T1,T2>(string InActionName, InputEvent InEvent, Action<Key, T1, T2> InHandler, T1 _param1, T2 _param2)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(InKey, _param1, _param2);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent, _proxy);
        }

        /// <summary>
        /// 绑定Action事件 回调携带3个自定义参数
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <param name="InActionName"></param>
        /// <param name="InEvent"></param>
        /// <param name="InHandler"></param>
        /// <param name="_param1"></param>
        /// <param name="_param2"></param>
        /// <param name="_param3"></param>
        public void BindAction<T1, T2, T3>(string InActionName, InputEvent InEvent, Action<T1, T2, T3> InHandler, T1 _param1, T2 _param2, T3 _param3)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(_param1, _param2, _param3);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent, _proxy);
        }

        /// <summary>
        /// 绑定Action事件， 回调携带Key及3个自定义参数
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <typeparam name="T3"></typeparam>
        /// <param name="InActionName"></param>
        /// <param name="InEvent"></param>
        /// <param name="InHandler"></param>
        /// <param name="_param1"></param>
        /// <param name="_param2"></param>
        /// <param name="_param3"></param>
        public void BindAction<T1, T2, T3>(string InActionName, InputEvent InEvent, Action<Key, T1, T2, T3> InHandler, T1 _param1, T2 _param2, T3 _param3)
        {
            NativeActionWithKeySignature _proxy = new NativeActionWithKeySignature((string InKey) =>
            {
                InHandler(InKey, _param1, _param2, _param3);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAction(this.ID, InActionName, (int)InEvent, _proxy);
        }

        /// <summary>
        /// 绑定Axis事件
        /// </summary>
        /// <param name="InAxisName">Axis 名称</param>
        /// <param name="InHandler">事件响应回调</param>
        public void BindAxis(string InAxisName, Action<float> InHandler)
        {
            NativeAxisSignature _proxy = new NativeAxisSignature((float InVal) =>
            {
                InHandler(InVal);
            });
            delegateRefs.Add(_proxy);
            IONativeWrapper.BindAxis(this.ID, InAxisName, _proxy);
        }

        /// <summary>
        /// 获取一个键的输出值
        /// </summary>
        /// <param name="InKey"></param>
        /// <returns></returns>
        public float GetDO(Key InKey)
        {
            return IONativeWrapper.GetDOSingle(this.ID, InKey);
        }

        /// <summary>
        /// 获取一个OAction输出值
        /// </summary>
        /// <param name="InOAction"></param>
        /// <returns></returns>
        public float GetDO(string InOAction)
        {
            return IONativeWrapper.GetDOAction(this.ID, InOAction);
        }

        /// <summary>
        /// 获取所有通道的输出值
        /// </summary>
        /// <param name="DOStatus"></param>
        /// <returns></returns>
        public int GetDO(float[] DOStatus)
        {
            return IONativeWrapper.GetDOAll(this.ID, DOStatus);
        }

        /// <summary>
        /// 设置一个键的输出值
        /// </summary>
        /// <param name="InKey"></param>
        /// <param name="InStatus"></param>
        /// <returns></returns>
        public int SetDO(Key InKey, float InStatus)
        {
            return IONativeWrapper.SetDOSingle(this.ID, InKey, InStatus);
        }

        /// <summary>
        /// 设置所有键的输出值
        /// </summary>
        /// <param name="InStatus"></param>
        /// <returns></returns>
        public int SetDO(float[] InStatus)
        {
            return IONativeWrapper.SetDOAll(this.ID, InStatus);
        }

        /// <summary>
        /// 设置指定OAction的输出值
        /// </summary>
        /// <param name="OAction">OAction 名称</param>
        /// <param name="InVal"></param>
        /// <param name="bIgnoreMassage"></param>
        /// <returns></returns>
        public int SetDO(string OAction, float InVal, bool bIgnoreMassage=false)
        {
            return IONativeWrapper.SetDOAction(this.ID, OAction, InVal, bIgnoreMassage);
        }

        /// <summary>
        /// 设置OAction的输出值为1
        /// </summary>
        /// <param name="OAction"></param>
        /// <returns></returns>
        public int SetDOOn(string OAction)
        {
            return IONativeWrapper.SetDOOn(this.ID, OAction);
        }

        /// <summary>
        /// 设置OAction的输出值为0
        /// </summary>
        /// <param name="OAction"></param>
        /// <returns></returns>
        public int SetDOOff(string OAction)
        {
            return IONativeWrapper.SetDOOff(this.ID, OAction);
        }

        /// <summary>
        /// 立即执行输出动作
        /// </summary>
        /// <returns></returns>
        public int DOImmeditate()
        {
            return IONativeWrapper.DOImmediate(this.ID);
        }

        /// <summary>
        /// 获取一个键是否被按下
        /// </summary>
        /// <param name="InKey"></param>
        /// <returns></returns>
        public bool GetKey(string InKey)
        {
            return IONativeWrapper.GetKey(this.ID, InKey);
        }

        /// <summary>
        /// 键被按下时响应一次
        /// </summary>
        /// <param name="InKey"></param>
        /// <returns></returns>
        public bool GetKeyDown(Key InKey)
        {
            return IONativeWrapper.GetKeyDown(this.ID, InKey);
        }

        /// <summary>
        /// 获取一个键是否弹起
        /// </summary>
        /// <param name="InKey"></param>
        /// <returns></returns>
        public bool GetKeyUp(Key InKey)
        {
            return IONativeWrapper.GetKeyUp(this.ID, InKey);
        }

        /// <summary>
        /// 获取Axis值
        /// </summary>
        /// <param name="InAxisName"></param>
        /// <returns></returns>
        public float GetAxis(string InAxisName)
        {
            return IONativeWrapper.GetAxis(this.ID, InAxisName);
        }

        /// <summary>
        /// 获取键的值
        /// </summary>
        /// <param name="InKey"></param>
        /// <returns></returns>
        public float GetAxisKey(Key InKey)
        {
            return IONativeWrapper.GetAxisKey(this.ID, InKey);
        }

        /// <summary>
        /// 获取一个键被按下的时长
        /// </summary>
        /// <param name="InKey"></param>
        /// <returns></returns>
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
