// ------------------------------------------------------------------------------
// 本文件为按槽位容量批量展开的样板代码（每一行同构），
// 仅在调整槽位容量时需要重新展开，日常修改请改 NativeCallbackBridge。
//
// 用途：IL2CPP 不接受指向实例方法的委托封送到原生代码，且要求每个被封送的方法标注
//       [AOT.MonoPInvokeCallback]。这里为每类回调预生成固定数量的静态 thunk，按槽位
//       索引转发到 NativeCallbackBridge 的登记表，并由 static readonly 数组持有委托
//       实例，保证 Mono 与 IL2CPP 下都不会被 GC 回收、也不会被代码裁剪。
//
// 槽位容量：Action 16 / ActionWithKey 128 / Axis 32 / ChannelMessage 64 / ChannelRequest 64
// 调整容量时须同步 NativeCallbackBridge 中的 *Capacity 常量。
// ------------------------------------------------------------------------------

using System;

namespace IOToolkit.Core
{
    // 该委托声明在 IONativeWrapper 内部，取别名以便本文件直接引用。
    using ChannelRequestCallback = IONativeWrapper.ChannelRequestCallback;

    internal static class NativeThunks
    {
        public static readonly NativeActionSignature[] ActionThunks = new NativeActionSignature[]
        {
            Action_0,
            Action_1,
            Action_2,
            Action_3,
            Action_4,
            Action_5,
            Action_6,
            Action_7,
            Action_8,
            Action_9,
            Action_10,
            Action_11,
            Action_12,
            Action_13,
            Action_14,
            Action_15,
        };

        public static readonly NativeActionWithKeySignature[] ActionWithKeyThunks = new NativeActionWithKeySignature[]
        {
            ActionWithKey_0,
            ActionWithKey_1,
            ActionWithKey_2,
            ActionWithKey_3,
            ActionWithKey_4,
            ActionWithKey_5,
            ActionWithKey_6,
            ActionWithKey_7,
            ActionWithKey_8,
            ActionWithKey_9,
            ActionWithKey_10,
            ActionWithKey_11,
            ActionWithKey_12,
            ActionWithKey_13,
            ActionWithKey_14,
            ActionWithKey_15,
            ActionWithKey_16,
            ActionWithKey_17,
            ActionWithKey_18,
            ActionWithKey_19,
            ActionWithKey_20,
            ActionWithKey_21,
            ActionWithKey_22,
            ActionWithKey_23,
            ActionWithKey_24,
            ActionWithKey_25,
            ActionWithKey_26,
            ActionWithKey_27,
            ActionWithKey_28,
            ActionWithKey_29,
            ActionWithKey_30,
            ActionWithKey_31,
            ActionWithKey_32,
            ActionWithKey_33,
            ActionWithKey_34,
            ActionWithKey_35,
            ActionWithKey_36,
            ActionWithKey_37,
            ActionWithKey_38,
            ActionWithKey_39,
            ActionWithKey_40,
            ActionWithKey_41,
            ActionWithKey_42,
            ActionWithKey_43,
            ActionWithKey_44,
            ActionWithKey_45,
            ActionWithKey_46,
            ActionWithKey_47,
            ActionWithKey_48,
            ActionWithKey_49,
            ActionWithKey_50,
            ActionWithKey_51,
            ActionWithKey_52,
            ActionWithKey_53,
            ActionWithKey_54,
            ActionWithKey_55,
            ActionWithKey_56,
            ActionWithKey_57,
            ActionWithKey_58,
            ActionWithKey_59,
            ActionWithKey_60,
            ActionWithKey_61,
            ActionWithKey_62,
            ActionWithKey_63,
            ActionWithKey_64,
            ActionWithKey_65,
            ActionWithKey_66,
            ActionWithKey_67,
            ActionWithKey_68,
            ActionWithKey_69,
            ActionWithKey_70,
            ActionWithKey_71,
            ActionWithKey_72,
            ActionWithKey_73,
            ActionWithKey_74,
            ActionWithKey_75,
            ActionWithKey_76,
            ActionWithKey_77,
            ActionWithKey_78,
            ActionWithKey_79,
            ActionWithKey_80,
            ActionWithKey_81,
            ActionWithKey_82,
            ActionWithKey_83,
            ActionWithKey_84,
            ActionWithKey_85,
            ActionWithKey_86,
            ActionWithKey_87,
            ActionWithKey_88,
            ActionWithKey_89,
            ActionWithKey_90,
            ActionWithKey_91,
            ActionWithKey_92,
            ActionWithKey_93,
            ActionWithKey_94,
            ActionWithKey_95,
            ActionWithKey_96,
            ActionWithKey_97,
            ActionWithKey_98,
            ActionWithKey_99,
            ActionWithKey_100,
            ActionWithKey_101,
            ActionWithKey_102,
            ActionWithKey_103,
            ActionWithKey_104,
            ActionWithKey_105,
            ActionWithKey_106,
            ActionWithKey_107,
            ActionWithKey_108,
            ActionWithKey_109,
            ActionWithKey_110,
            ActionWithKey_111,
            ActionWithKey_112,
            ActionWithKey_113,
            ActionWithKey_114,
            ActionWithKey_115,
            ActionWithKey_116,
            ActionWithKey_117,
            ActionWithKey_118,
            ActionWithKey_119,
            ActionWithKey_120,
            ActionWithKey_121,
            ActionWithKey_122,
            ActionWithKey_123,
            ActionWithKey_124,
            ActionWithKey_125,
            ActionWithKey_126,
            ActionWithKey_127,
        };

        public static readonly NativeAxisSignature[] AxisThunks = new NativeAxisSignature[]
        {
            Axis_0,
            Axis_1,
            Axis_2,
            Axis_3,
            Axis_4,
            Axis_5,
            Axis_6,
            Axis_7,
            Axis_8,
            Axis_9,
            Axis_10,
            Axis_11,
            Axis_12,
            Axis_13,
            Axis_14,
            Axis_15,
            Axis_16,
            Axis_17,
            Axis_18,
            Axis_19,
            Axis_20,
            Axis_21,
            Axis_22,
            Axis_23,
            Axis_24,
            Axis_25,
            Axis_26,
            Axis_27,
            Axis_28,
            Axis_29,
            Axis_30,
            Axis_31,
        };

        public static readonly PluginChannelCallback[] ChannelMessageThunks = new PluginChannelCallback[]
        {
            ChannelMessage_0,
            ChannelMessage_1,
            ChannelMessage_2,
            ChannelMessage_3,
            ChannelMessage_4,
            ChannelMessage_5,
            ChannelMessage_6,
            ChannelMessage_7,
            ChannelMessage_8,
            ChannelMessage_9,
            ChannelMessage_10,
            ChannelMessage_11,
            ChannelMessage_12,
            ChannelMessage_13,
            ChannelMessage_14,
            ChannelMessage_15,
            ChannelMessage_16,
            ChannelMessage_17,
            ChannelMessage_18,
            ChannelMessage_19,
            ChannelMessage_20,
            ChannelMessage_21,
            ChannelMessage_22,
            ChannelMessage_23,
            ChannelMessage_24,
            ChannelMessage_25,
            ChannelMessage_26,
            ChannelMessage_27,
            ChannelMessage_28,
            ChannelMessage_29,
            ChannelMessage_30,
            ChannelMessage_31,
            ChannelMessage_32,
            ChannelMessage_33,
            ChannelMessage_34,
            ChannelMessage_35,
            ChannelMessage_36,
            ChannelMessage_37,
            ChannelMessage_38,
            ChannelMessage_39,
            ChannelMessage_40,
            ChannelMessage_41,
            ChannelMessage_42,
            ChannelMessage_43,
            ChannelMessage_44,
            ChannelMessage_45,
            ChannelMessage_46,
            ChannelMessage_47,
            ChannelMessage_48,
            ChannelMessage_49,
            ChannelMessage_50,
            ChannelMessage_51,
            ChannelMessage_52,
            ChannelMessage_53,
            ChannelMessage_54,
            ChannelMessage_55,
            ChannelMessage_56,
            ChannelMessage_57,
            ChannelMessage_58,
            ChannelMessage_59,
            ChannelMessage_60,
            ChannelMessage_61,
            ChannelMessage_62,
            ChannelMessage_63,
        };

        public static readonly ChannelRequestCallback[] ChannelRequestThunks = new ChannelRequestCallback[]
        {
            ChannelRequest_0,
            ChannelRequest_1,
            ChannelRequest_2,
            ChannelRequest_3,
            ChannelRequest_4,
            ChannelRequest_5,
            ChannelRequest_6,
            ChannelRequest_7,
            ChannelRequest_8,
            ChannelRequest_9,
            ChannelRequest_10,
            ChannelRequest_11,
            ChannelRequest_12,
            ChannelRequest_13,
            ChannelRequest_14,
            ChannelRequest_15,
            ChannelRequest_16,
            ChannelRequest_17,
            ChannelRequest_18,
            ChannelRequest_19,
            ChannelRequest_20,
            ChannelRequest_21,
            ChannelRequest_22,
            ChannelRequest_23,
            ChannelRequest_24,
            ChannelRequest_25,
            ChannelRequest_26,
            ChannelRequest_27,
            ChannelRequest_28,
            ChannelRequest_29,
            ChannelRequest_30,
            ChannelRequest_31,
            ChannelRequest_32,
            ChannelRequest_33,
            ChannelRequest_34,
            ChannelRequest_35,
            ChannelRequest_36,
            ChannelRequest_37,
            ChannelRequest_38,
            ChannelRequest_39,
            ChannelRequest_40,
            ChannelRequest_41,
            ChannelRequest_42,
            ChannelRequest_43,
            ChannelRequest_44,
            ChannelRequest_45,
            ChannelRequest_46,
            ChannelRequest_47,
            ChannelRequest_48,
            ChannelRequest_49,
            ChannelRequest_50,
            ChannelRequest_51,
            ChannelRequest_52,
            ChannelRequest_53,
            ChannelRequest_54,
            ChannelRequest_55,
            ChannelRequest_56,
            ChannelRequest_57,
            ChannelRequest_58,
            ChannelRequest_59,
            ChannelRequest_60,
            ChannelRequest_61,
            ChannelRequest_62,
            ChannelRequest_63,
        };

        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_0() { NativeCallbackBridge.InvokeAction(0); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_1() { NativeCallbackBridge.InvokeAction(1); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_2() { NativeCallbackBridge.InvokeAction(2); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_3() { NativeCallbackBridge.InvokeAction(3); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_4() { NativeCallbackBridge.InvokeAction(4); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_5() { NativeCallbackBridge.InvokeAction(5); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_6() { NativeCallbackBridge.InvokeAction(6); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_7() { NativeCallbackBridge.InvokeAction(7); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_8() { NativeCallbackBridge.InvokeAction(8); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_9() { NativeCallbackBridge.InvokeAction(9); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_10() { NativeCallbackBridge.InvokeAction(10); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_11() { NativeCallbackBridge.InvokeAction(11); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_12() { NativeCallbackBridge.InvokeAction(12); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_13() { NativeCallbackBridge.InvokeAction(13); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_14() { NativeCallbackBridge.InvokeAction(14); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionSignature))]
        private static void Action_15() { NativeCallbackBridge.InvokeAction(15); }

        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_0(string key) { NativeCallbackBridge.InvokeActionWithKey(0, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_1(string key) { NativeCallbackBridge.InvokeActionWithKey(1, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_2(string key) { NativeCallbackBridge.InvokeActionWithKey(2, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_3(string key) { NativeCallbackBridge.InvokeActionWithKey(3, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_4(string key) { NativeCallbackBridge.InvokeActionWithKey(4, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_5(string key) { NativeCallbackBridge.InvokeActionWithKey(5, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_6(string key) { NativeCallbackBridge.InvokeActionWithKey(6, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_7(string key) { NativeCallbackBridge.InvokeActionWithKey(7, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_8(string key) { NativeCallbackBridge.InvokeActionWithKey(8, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_9(string key) { NativeCallbackBridge.InvokeActionWithKey(9, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_10(string key) { NativeCallbackBridge.InvokeActionWithKey(10, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_11(string key) { NativeCallbackBridge.InvokeActionWithKey(11, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_12(string key) { NativeCallbackBridge.InvokeActionWithKey(12, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_13(string key) { NativeCallbackBridge.InvokeActionWithKey(13, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_14(string key) { NativeCallbackBridge.InvokeActionWithKey(14, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_15(string key) { NativeCallbackBridge.InvokeActionWithKey(15, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_16(string key) { NativeCallbackBridge.InvokeActionWithKey(16, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_17(string key) { NativeCallbackBridge.InvokeActionWithKey(17, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_18(string key) { NativeCallbackBridge.InvokeActionWithKey(18, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_19(string key) { NativeCallbackBridge.InvokeActionWithKey(19, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_20(string key) { NativeCallbackBridge.InvokeActionWithKey(20, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_21(string key) { NativeCallbackBridge.InvokeActionWithKey(21, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_22(string key) { NativeCallbackBridge.InvokeActionWithKey(22, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_23(string key) { NativeCallbackBridge.InvokeActionWithKey(23, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_24(string key) { NativeCallbackBridge.InvokeActionWithKey(24, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_25(string key) { NativeCallbackBridge.InvokeActionWithKey(25, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_26(string key) { NativeCallbackBridge.InvokeActionWithKey(26, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_27(string key) { NativeCallbackBridge.InvokeActionWithKey(27, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_28(string key) { NativeCallbackBridge.InvokeActionWithKey(28, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_29(string key) { NativeCallbackBridge.InvokeActionWithKey(29, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_30(string key) { NativeCallbackBridge.InvokeActionWithKey(30, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_31(string key) { NativeCallbackBridge.InvokeActionWithKey(31, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_32(string key) { NativeCallbackBridge.InvokeActionWithKey(32, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_33(string key) { NativeCallbackBridge.InvokeActionWithKey(33, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_34(string key) { NativeCallbackBridge.InvokeActionWithKey(34, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_35(string key) { NativeCallbackBridge.InvokeActionWithKey(35, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_36(string key) { NativeCallbackBridge.InvokeActionWithKey(36, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_37(string key) { NativeCallbackBridge.InvokeActionWithKey(37, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_38(string key) { NativeCallbackBridge.InvokeActionWithKey(38, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_39(string key) { NativeCallbackBridge.InvokeActionWithKey(39, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_40(string key) { NativeCallbackBridge.InvokeActionWithKey(40, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_41(string key) { NativeCallbackBridge.InvokeActionWithKey(41, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_42(string key) { NativeCallbackBridge.InvokeActionWithKey(42, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_43(string key) { NativeCallbackBridge.InvokeActionWithKey(43, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_44(string key) { NativeCallbackBridge.InvokeActionWithKey(44, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_45(string key) { NativeCallbackBridge.InvokeActionWithKey(45, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_46(string key) { NativeCallbackBridge.InvokeActionWithKey(46, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_47(string key) { NativeCallbackBridge.InvokeActionWithKey(47, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_48(string key) { NativeCallbackBridge.InvokeActionWithKey(48, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_49(string key) { NativeCallbackBridge.InvokeActionWithKey(49, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_50(string key) { NativeCallbackBridge.InvokeActionWithKey(50, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_51(string key) { NativeCallbackBridge.InvokeActionWithKey(51, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_52(string key) { NativeCallbackBridge.InvokeActionWithKey(52, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_53(string key) { NativeCallbackBridge.InvokeActionWithKey(53, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_54(string key) { NativeCallbackBridge.InvokeActionWithKey(54, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_55(string key) { NativeCallbackBridge.InvokeActionWithKey(55, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_56(string key) { NativeCallbackBridge.InvokeActionWithKey(56, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_57(string key) { NativeCallbackBridge.InvokeActionWithKey(57, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_58(string key) { NativeCallbackBridge.InvokeActionWithKey(58, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_59(string key) { NativeCallbackBridge.InvokeActionWithKey(59, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_60(string key) { NativeCallbackBridge.InvokeActionWithKey(60, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_61(string key) { NativeCallbackBridge.InvokeActionWithKey(61, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_62(string key) { NativeCallbackBridge.InvokeActionWithKey(62, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_63(string key) { NativeCallbackBridge.InvokeActionWithKey(63, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_64(string key) { NativeCallbackBridge.InvokeActionWithKey(64, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_65(string key) { NativeCallbackBridge.InvokeActionWithKey(65, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_66(string key) { NativeCallbackBridge.InvokeActionWithKey(66, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_67(string key) { NativeCallbackBridge.InvokeActionWithKey(67, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_68(string key) { NativeCallbackBridge.InvokeActionWithKey(68, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_69(string key) { NativeCallbackBridge.InvokeActionWithKey(69, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_70(string key) { NativeCallbackBridge.InvokeActionWithKey(70, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_71(string key) { NativeCallbackBridge.InvokeActionWithKey(71, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_72(string key) { NativeCallbackBridge.InvokeActionWithKey(72, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_73(string key) { NativeCallbackBridge.InvokeActionWithKey(73, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_74(string key) { NativeCallbackBridge.InvokeActionWithKey(74, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_75(string key) { NativeCallbackBridge.InvokeActionWithKey(75, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_76(string key) { NativeCallbackBridge.InvokeActionWithKey(76, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_77(string key) { NativeCallbackBridge.InvokeActionWithKey(77, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_78(string key) { NativeCallbackBridge.InvokeActionWithKey(78, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_79(string key) { NativeCallbackBridge.InvokeActionWithKey(79, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_80(string key) { NativeCallbackBridge.InvokeActionWithKey(80, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_81(string key) { NativeCallbackBridge.InvokeActionWithKey(81, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_82(string key) { NativeCallbackBridge.InvokeActionWithKey(82, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_83(string key) { NativeCallbackBridge.InvokeActionWithKey(83, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_84(string key) { NativeCallbackBridge.InvokeActionWithKey(84, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_85(string key) { NativeCallbackBridge.InvokeActionWithKey(85, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_86(string key) { NativeCallbackBridge.InvokeActionWithKey(86, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_87(string key) { NativeCallbackBridge.InvokeActionWithKey(87, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_88(string key) { NativeCallbackBridge.InvokeActionWithKey(88, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_89(string key) { NativeCallbackBridge.InvokeActionWithKey(89, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_90(string key) { NativeCallbackBridge.InvokeActionWithKey(90, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_91(string key) { NativeCallbackBridge.InvokeActionWithKey(91, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_92(string key) { NativeCallbackBridge.InvokeActionWithKey(92, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_93(string key) { NativeCallbackBridge.InvokeActionWithKey(93, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_94(string key) { NativeCallbackBridge.InvokeActionWithKey(94, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_95(string key) { NativeCallbackBridge.InvokeActionWithKey(95, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_96(string key) { NativeCallbackBridge.InvokeActionWithKey(96, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_97(string key) { NativeCallbackBridge.InvokeActionWithKey(97, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_98(string key) { NativeCallbackBridge.InvokeActionWithKey(98, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_99(string key) { NativeCallbackBridge.InvokeActionWithKey(99, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_100(string key) { NativeCallbackBridge.InvokeActionWithKey(100, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_101(string key) { NativeCallbackBridge.InvokeActionWithKey(101, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_102(string key) { NativeCallbackBridge.InvokeActionWithKey(102, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_103(string key) { NativeCallbackBridge.InvokeActionWithKey(103, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_104(string key) { NativeCallbackBridge.InvokeActionWithKey(104, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_105(string key) { NativeCallbackBridge.InvokeActionWithKey(105, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_106(string key) { NativeCallbackBridge.InvokeActionWithKey(106, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_107(string key) { NativeCallbackBridge.InvokeActionWithKey(107, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_108(string key) { NativeCallbackBridge.InvokeActionWithKey(108, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_109(string key) { NativeCallbackBridge.InvokeActionWithKey(109, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_110(string key) { NativeCallbackBridge.InvokeActionWithKey(110, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_111(string key) { NativeCallbackBridge.InvokeActionWithKey(111, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_112(string key) { NativeCallbackBridge.InvokeActionWithKey(112, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_113(string key) { NativeCallbackBridge.InvokeActionWithKey(113, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_114(string key) { NativeCallbackBridge.InvokeActionWithKey(114, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_115(string key) { NativeCallbackBridge.InvokeActionWithKey(115, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_116(string key) { NativeCallbackBridge.InvokeActionWithKey(116, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_117(string key) { NativeCallbackBridge.InvokeActionWithKey(117, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_118(string key) { NativeCallbackBridge.InvokeActionWithKey(118, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_119(string key) { NativeCallbackBridge.InvokeActionWithKey(119, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_120(string key) { NativeCallbackBridge.InvokeActionWithKey(120, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_121(string key) { NativeCallbackBridge.InvokeActionWithKey(121, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_122(string key) { NativeCallbackBridge.InvokeActionWithKey(122, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_123(string key) { NativeCallbackBridge.InvokeActionWithKey(123, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_124(string key) { NativeCallbackBridge.InvokeActionWithKey(124, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_125(string key) { NativeCallbackBridge.InvokeActionWithKey(125, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_126(string key) { NativeCallbackBridge.InvokeActionWithKey(126, key); }
        [AOT.MonoPInvokeCallback(typeof(NativeActionWithKeySignature))]
        private static void ActionWithKey_127(string key) { NativeCallbackBridge.InvokeActionWithKey(127, key); }

        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_0(float value) { NativeCallbackBridge.InvokeAxis(0, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_1(float value) { NativeCallbackBridge.InvokeAxis(1, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_2(float value) { NativeCallbackBridge.InvokeAxis(2, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_3(float value) { NativeCallbackBridge.InvokeAxis(3, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_4(float value) { NativeCallbackBridge.InvokeAxis(4, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_5(float value) { NativeCallbackBridge.InvokeAxis(5, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_6(float value) { NativeCallbackBridge.InvokeAxis(6, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_7(float value) { NativeCallbackBridge.InvokeAxis(7, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_8(float value) { NativeCallbackBridge.InvokeAxis(8, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_9(float value) { NativeCallbackBridge.InvokeAxis(9, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_10(float value) { NativeCallbackBridge.InvokeAxis(10, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_11(float value) { NativeCallbackBridge.InvokeAxis(11, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_12(float value) { NativeCallbackBridge.InvokeAxis(12, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_13(float value) { NativeCallbackBridge.InvokeAxis(13, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_14(float value) { NativeCallbackBridge.InvokeAxis(14, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_15(float value) { NativeCallbackBridge.InvokeAxis(15, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_16(float value) { NativeCallbackBridge.InvokeAxis(16, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_17(float value) { NativeCallbackBridge.InvokeAxis(17, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_18(float value) { NativeCallbackBridge.InvokeAxis(18, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_19(float value) { NativeCallbackBridge.InvokeAxis(19, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_20(float value) { NativeCallbackBridge.InvokeAxis(20, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_21(float value) { NativeCallbackBridge.InvokeAxis(21, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_22(float value) { NativeCallbackBridge.InvokeAxis(22, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_23(float value) { NativeCallbackBridge.InvokeAxis(23, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_24(float value) { NativeCallbackBridge.InvokeAxis(24, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_25(float value) { NativeCallbackBridge.InvokeAxis(25, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_26(float value) { NativeCallbackBridge.InvokeAxis(26, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_27(float value) { NativeCallbackBridge.InvokeAxis(27, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_28(float value) { NativeCallbackBridge.InvokeAxis(28, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_29(float value) { NativeCallbackBridge.InvokeAxis(29, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_30(float value) { NativeCallbackBridge.InvokeAxis(30, value); }
        [AOT.MonoPInvokeCallback(typeof(NativeAxisSignature))]
        private static void Axis_31(float value) { NativeCallbackBridge.InvokeAxis(31, value); }

        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_0(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(0, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_1(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(1, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_2(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(2, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_3(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(3, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_4(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(4, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_5(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(5, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_6(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(6, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_7(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(7, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_8(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(8, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_9(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(9, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_10(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(10, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_11(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(11, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_12(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(12, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_13(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(13, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_14(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(14, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_15(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(15, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_16(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(16, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_17(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(17, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_18(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(18, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_19(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(19, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_20(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(20, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_21(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(21, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_22(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(22, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_23(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(23, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_24(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(24, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_25(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(25, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_26(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(26, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_27(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(27, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_28(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(28, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_29(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(29, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_30(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(30, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_31(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(31, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_32(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(32, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_33(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(33, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_34(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(34, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_35(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(35, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_36(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(36, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_37(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(37, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_38(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(38, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_39(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(39, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_40(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(40, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_41(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(41, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_42(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(42, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_43(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(43, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_44(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(44, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_45(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(45, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_46(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(46, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_47(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(47, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_48(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(48, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_49(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(49, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_50(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(50, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_51(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(51, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_52(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(52, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_53(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(53, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_54(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(54, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_55(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(55, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_56(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(56, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_57(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(57, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_58(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(58, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_59(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(59, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_60(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(60, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_61(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(61, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_62(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(62, ch, d, s); }
        [AOT.MonoPInvokeCallback(typeof(PluginChannelCallback))]
        private static void ChannelMessage_63(string ch, IntPtr d, uint s) { NativeCallbackBridge.InvokeChannelMessage(63, ch, d, s); }

        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_0(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(0, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_1(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(1, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_2(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(2, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_3(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(3, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_4(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(4, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_5(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(5, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_6(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(6, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_7(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(7, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_8(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(8, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_9(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(9, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_10(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(10, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_11(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(11, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_12(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(12, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_13(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(13, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_14(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(14, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_15(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(15, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_16(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(16, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_17(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(17, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_18(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(18, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_19(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(19, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_20(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(20, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_21(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(21, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_22(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(22, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_23(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(23, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_24(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(24, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_25(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(25, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_26(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(26, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_27(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(27, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_28(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(28, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_29(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(29, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_30(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(30, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_31(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(31, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_32(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(32, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_33(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(33, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_34(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(34, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_35(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(35, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_36(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(36, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_37(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(37, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_38(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(38, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_39(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(39, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_40(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(40, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_41(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(41, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_42(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(42, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_43(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(43, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_44(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(44, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_45(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(45, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_46(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(46, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_47(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(47, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_48(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(48, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_49(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(49, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_50(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(50, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_51(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(51, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_52(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(52, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_53(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(53, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_54(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(54, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_55(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(55, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_56(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(56, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_57(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(57, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_58(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(58, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_59(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(59, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_60(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(60, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_61(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(61, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_62(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(62, nm, rid, p, ps, m, ms); }
        [AOT.MonoPInvokeCallback(typeof(ChannelRequestCallback))]
        private static void ChannelRequest_63(string nm, string rid, IntPtr p, uint ps, IntPtr m, uint ms) { NativeCallbackBridge.InvokeChannelRequest(63, nm, rid, p, ps, m, ms); }

    }
}
