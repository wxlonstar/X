﻿using System;
using System.Diagnostics;
using System.IO;
using NewLife.Linq;

namespace NewLife.Serialization
{
    /// <summary>读写器基类</summary>
    public abstract class ReaderWriterBase<TSettings> : /*NewLife.DisposeBase,*/ IReaderWriter where TSettings : ReaderWriterSetting, new()
    {
        #region 属性
        private String _Name;
        /// <summary>读写器名称</summary>
        public virtual String Name
        {
            get
            {
                if (String.IsNullOrEmpty(_Name))
                {
                    _Name = this.GetType().Name;
                    if (_Name.EndsWith("Reader", StringComparison.OrdinalIgnoreCase))
                        _Name = _Name.Substring(0, _Name.Length - "Reader".Length);
                    if (_Name.EndsWith("Writer", StringComparison.OrdinalIgnoreCase))
                        _Name = _Name.Substring(0, _Name.Length - "Writer".Length);
                }
                return _Name;
            }
        }

        private Stream _Stream;
        /// <summary>数据流。默认实例化一个MemoryStream，设置值时将重置Depth为1</summary>
        public virtual Stream Stream
        {
            get { return _Stream ?? (_Stream = new MemoryStream()); }
            set
            {
                if (_Stream != value)
                {
                    Depth = 1;

                    _Stream = value;
                }
            }
        }

        private TSettings _Settings;
        /// <summary>序列化设置</summary>
        public virtual TSettings Settings
        {
            get { return _Settings ?? (_Settings = new TSettings()); }
            set { _Settings = value; }
        }

        /// <summary>序列化设置</summary>
        ReaderWriterSetting IReaderWriter.Settings { get { return Settings; } set { Settings = (TSettings)value; } }

        private Int32 _Depth;
        /// <summary>层次深度</summary>
        public Int32 Depth
        {
            get
            {
                if (_Depth < 1) _Depth = 1;
                return _Depth;
            }
            set { _Depth = value; }
        }

        /// <summary>是否使用大小，如果使用，将在写入数组、集合和字符串前预先写入大小</summary>
        protected virtual Boolean UseSize { get { return true; } }

        private Object _CurrentObject;
        /// <summary>当前对象</summary>
        internal protected Object CurrentObject { get { return _CurrentObject; } set { _CurrentObject = value; } }

        private IObjectMemberInfo _CurrentMember;
        /// <summary>当前成员</summary>
        internal protected IObjectMemberInfo CurrentMember { get { return _CurrentMember; } set { _CurrentMember = value; } }
        #endregion

        #region 方法
        /// <summary>重置</summary>
        public virtual void Reset() { Depth = 1; }

        /// <summary>是否精确类型</summary>
        /// <param name="type"></param>
        /// <returns></returns>
        protected static Boolean IsExactType(Type type)
        {
            if (type == null || type.IsInterface || type.IsAbstract || type == typeof(Object))
                return false;
            else
                return true;
        }
        #endregion

        #region 获取成员
        /// <summary>
        /// 获取需要序列化的成员
        /// </summary>
        /// <param name="type">类型</param>
        /// <param name="value">对象</param>
        /// <returns>需要序列化的成员</returns>
        public IObjectMemberInfo[] GetMembers(Type type, Object value)
        {
            if (type == null) throw new ArgumentNullException("type");

            IObjectMemberInfo[] mis = OnGetMembers(type, value);

            if (OnGotMembers != null)
            {
                EventArgs<Type, Object, IObjectMemberInfo[]> e = new EventArgs<Type, Object, IObjectMemberInfo[]>(type, value, mis);
                OnGotMembers(this, e);
                mis = e.Arg3;
            }

            // 过滤掉被忽略的成员
            var members = Settings.IgnoreMembers;
            if (members.Count > 0)
            {
                mis = mis.Where(m => !members.Contains(m.Name)).ToArray();
            }

            return mis;
        }

        /// <summary>获取需要序列化的成员（属性或字段）</summary>
        /// <param name="type">指定类型</param>
        /// <param name="value">对象</param>
        /// <returns>需要序列化的成员</returns>
        protected virtual IObjectMemberInfo[] OnGetMembers(Type type, Object value)
        {
            if (type == null) throw new ArgumentNullException("type");

            return ObjectInfo.GetMembers(type, value, Settings.UseField, Settings.IsBaseFirst);
        }

        /// <summary>
        /// 获取指定类型中需要序列化的成员时触发。使用者可以修改、排序要序列化的成员。
        /// </summary>
        public event EventHandler<EventArgs<Type, Object, IObjectMemberInfo[]>> OnGotMembers;
        #endregion

        #region 对象默认值
        /// <summary>
        /// 判断一个对象的某个成员是否默认值
        /// </summary>
        /// <param name="value"></param>
        /// <param name="member"></param>
        /// <returns></returns>
        internal static Boolean IsDefault(Object value, IObjectMemberInfo member)
        {
            if (value == null) return false;

            Object def = ObjectInfo.GetDefaultObject(value.GetType());
            return Object.Equals(member[value], member[def]);
        }
        #endregion

        #region 释放
        ///// <summary>
        ///// 释放资源
        ///// </summary>
        ///// <param name="disposing"></param>
        //protected override void OnDispose(bool disposing)
        //{
        //    base.OnDispose(disposing);
        //}
        #endregion

        #region 写日志
        /// <summary>
        /// 调试输出
        /// </summary>
        /// <param name="action">操作</param>
        /// <param name="args">参数</param>
        [Conditional("DEBUG")]
        public void WriteLog(String action, params Object[] args)
        {
            WriteLog(0, action, args);
        }

        static ConsoleColor[][] colors = new ConsoleColor[][] { 
            new ConsoleColor[] { ConsoleColor.Green,ConsoleColor.Magenta, ConsoleColor.White, ConsoleColor.Yellow },
            new ConsoleColor[] { ConsoleColor.Yellow, ConsoleColor.White, ConsoleColor.Magenta,ConsoleColor.Green }
        };

        static Boolean? _IsConsole;
        /// <summary>是否控制台</summary>
        static Boolean IsConsole { get { return (_IsConsole ?? (_IsConsole = Runtime.IsConsole)).Value; } }

        /// <summary>
        /// 调试输出
        /// </summary>
        /// <param name="colorIndex">颜色方案</param>
        /// <param name="action">操作</param>
        /// <param name="args">参数</param>
        [Conditional("DEBUG")]
        public void WriteLog(Int32 colorIndex, String action, params Object[] args)
        {
            if (!IsConsole) return;

            ConsoleColor color = Console.ForegroundColor;

            // 缩进
            SetDebugIndent();

            // 红色动作
            Console.ForegroundColor = colors[colorIndex][0];
            Console.Write(action);

            if (args != null && args.Length > 0)
            {
                // 白色参数
                //Console.ForegroundColor = ConsoleColor.White;

                for (int i = 0; i < args.Length; i++)
                {
                    Console.ForegroundColor = colors[colorIndex][i % colors.Length + 1];
                    Console.Write(" ");
                    Console.Write(args[i]);
                }
            }

            Console.ForegroundColor = color;
            Console.WriteLine();
        }

        /// <summary>
        /// 设置调试缩进
        /// </summary>
        /// <param name="indent">缩进</param>
        [Conditional("DEBUG")]
        public static void SetDebugIndent(Int32 indent)
        {
            if (!IsConsole) return;

            try
            {
                Console.CursorLeft = indent * 4;
            }
            catch { }
        }

        /// <summary>
        /// 设置调试缩进
        /// </summary>
        [Conditional("DEBUG")]
        public void SetDebugIndent()
        {
            if (!IsConsole) return;

            SetDebugIndent(Depth - 1);
        }
        #endregion
    }
}