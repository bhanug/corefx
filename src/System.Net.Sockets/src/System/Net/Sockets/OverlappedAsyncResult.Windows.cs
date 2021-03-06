// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using System.Collections.Generic;

namespace System.Net.Sockets
{
    // OverlappedAsyncResult
    //
    // This class is used to take care of storage for async Socket operation
    // from the BeginSend, BeginSendTo, BeginReceive, BeginReceiveFrom calls.
    internal partial class OverlappedAsyncResult : BaseOverlappedAsyncResult
    {
        internal WSABuffer _singleBuffer;
        internal WSABuffer[] _wsaBuffers;

        internal IntPtr GetSocketAddressPtr()
        {
            return Marshal.UnsafeAddrOfPinnedArrayElement(_socketAddress.Buffer, 0);
        }

        internal IntPtr GetSocketAddressSizePtr()
        {
            return Marshal.UnsafeAddrOfPinnedArrayElement(_socketAddress.Buffer, _socketAddress.GetAddressSizeOffset());
        }

        internal unsafe int GetSocketAddressSize()
        {
            return *(int*)GetSocketAddressSizePtr();
        }

        // SetUnmanagedStructures
        //
        // Fills in overlapped structures used in an async overlapped Winsock call.
        // These calls are outside the runtime and are unmanaged code, so we need
        // to prepare specific structures and ints that lie in unmanaged memory
        // since the overlapped calls may complete asynchronously.
        internal void SetUnmanagedStructures(byte[] buffer, int offset, int size, Internals.SocketAddress socketAddress, bool pinSocketAddress)
        {
            // Fill in Buffer Array structure that will be used for our send/recv Buffer
            _socketAddress = socketAddress;
            if (pinSocketAddress && _socketAddress != null)
            {
                object[] objectsToPin = null;
                objectsToPin = new object[2];
                objectsToPin[0] = buffer;

                _socketAddress.CopyAddressSizeIntoBuffer();
                objectsToPin[1] = _socketAddress.Buffer;

                base.SetUnmanagedStructures(objectsToPin);
            }
            else
            {
                base.SetUnmanagedStructures(buffer);
            }

            _singleBuffer.Length = size;
            _singleBuffer.Pointer = Marshal.UnsafeAddrOfPinnedArrayElement(buffer, offset);
        }

        internal void SetUnmanagedStructures(IList<ArraySegment<byte>> buffers)
        {
            // Fill in Buffer Array structure that will be used for our send/recv Buffer.
            // Make sure we don't let the app mess up the buffer array enough to cause
            // corruption.
            int count = buffers.Count;
            ArraySegment<byte>[] buffersCopy = new ArraySegment<byte>[count];

            for (int i = 0; i < count; i++)
            {
                buffersCopy[i] = buffers[i];
                RangeValidationHelpers.ValidateSegment(buffersCopy[i]);
            }

            _wsaBuffers = new WSABuffer[count];

            object[] objectsToPin = new object[count];
            for (int i = 0; i < count; i++)
            {
                objectsToPin[i] = buffersCopy[i].Array;
            }

            base.SetUnmanagedStructures(objectsToPin);

            for (int i = 0; i < count; i++)
            {
                _wsaBuffers[i].Length = buffersCopy[i].Count;
                _wsaBuffers[i].Pointer = Marshal.UnsafeAddrOfPinnedArrayElement(buffersCopy[i].Array, buffersCopy[i].Offset);
            }
        }

        // This method is called after an asynchronous call is made for the user.
        // It checks and acts accordingly if the IO:
        // 1) completed synchronously.
        // 2) was pended.
        // 3) failed.
        internal override object PostCompletion(int numBytes)
        {
            if (ErrorCode == 0 && SocketsEventSource.Log.IsEnabled())
            {
                LogBuffer(numBytes);
            }

            return (int)numBytes;
        }

        private void LogBuffer(int size)
        {
            GlobalLog.Assert(SocketsEventSource.Log.IsEnabled(), "OverlappedAsyncResult#{0}::LogBuffer()|Logging is off!", LoggingHash.HashString(this));
            if (size > -1)
            {
                if (_wsaBuffers != null)
                {
                    foreach (WSABuffer wsaBuffer in _wsaBuffers)
                    {
                        SocketsEventSource.Dump(SocketsEventSource.MethodType.PostCompletion, wsaBuffer.Pointer, Math.Min(wsaBuffer.Length, size));
                        if ((size -= wsaBuffer.Length) <= 0)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    SocketsEventSource.Dump(SocketsEventSource.MethodType.PostCompletion, _singleBuffer.Pointer, Math.Min(_singleBuffer.Length, size));
                }
            }
        }
    }
}
