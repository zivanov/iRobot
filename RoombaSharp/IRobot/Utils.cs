﻿/*
 MIT License

Copyright (c) [2016] [Orlin Dimitrov]

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial SerialPortions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace iRobot
{
    public static class Utils
    {

        /// <summary>
        /// Converts byte aray to structure.
        /// </summary>
        /// <typeparam name="T">Type of the structure.</typeparam>
        /// <param name="bytes">Bytes for the structure.</param>
        /// <returns>Type of the structure.</returns>
        public static T ByteArrayToStructure<T>(byte[] bytes) where T : struct
        {
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            var result = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            handle.Free();
            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public static byte BitSet(byte value, byte index)
        {
            return (byte)(value | (1 << index));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public static byte BitClear(byte value, byte index)
        {
            return (byte)(value & ~(1 << index));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public static byte BitToggle(byte value, byte index)
        {
            return (byte)(value ^ (1 << index));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public static bool GetBit(this byte value, int index)
        {
            return (value & (1 << index)) != 0;
        }
    }
}