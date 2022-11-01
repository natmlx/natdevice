/* 
*   NatDevice
*   Copyright (c) 2022 NatML Inc. All Rights Reserved.
*/

namespace NatML.Devices.Outputs {

    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using UnityEngine;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Internal;

    /// <summary>
    /// Camera device output that converts camera images into RGBA8888 pixel buffers.
    /// </summary>
    public sealed class PixelBufferOutput : CameraOutput {

        #region --Client API--
        /// <summary>
        /// Pixel buffer conversion options.
        /// </summary>
        public class ConversionOptions {
            /// <summary>
            /// Desired pixel buffer orientation.
            /// </summary>
            public ScreenOrientation orientation;
            /// <summary>
            /// Whether to vertically mirror the pixel buffer.
            /// </summary>
            public bool mirror;
        }

        /// <summary>
        /// Pixel buffer with latest camera image.
        /// The pixel buffer is always laid out in RGBA8888 format.
        /// </summary>
        public NativeArray<byte> pixelBuffer => convertedBuffer;

        /// <summary>
        /// Pixel buffer width.
        /// </summary>
        public int width { get; private set; }

        /// <summary>
        /// Pixel buffer height.
        /// </summary>
        public int height { get; private set; }

        /// <summary>
        /// Pixel buffer timestamp.
        /// This is the timestamp of the image in the pixel buffer.
        /// </summary>
        public long timestamp { get; private set; }

        /// <summary>
        /// Get or set the pixel buffer orientation.
        /// </summary>
        public ScreenOrientation orientation;

        /// <summary>
        /// Create a pixel buffer output.
        /// </summary>
        public PixelBufferOutput () {
            this.orientation = OrientationSupport.Contains(Application.platform) ? Screen.orientation : 0;
            this.lifecycleHelper = LifecycleHelper.Create();
            this.fence = new object();
            lifecycleHelper.onQuit += Dispose;
        }

        /// <summary>
        /// Update the output with a new camera image.
        /// </summary>
        /// <param name="image">Camera image.</param>
        public override void Update (CameraImage image) => Update(image, null);

        /// <summary>
        /// Update the output with a new camera image.
        /// </summary>
        /// <param name="image">Camera image.</param>
        /// <param name="options">Conversion options.</param>
        public unsafe void Update (CameraImage image, ConversionOptions options) {
            lock (fence) {
                // Check
                if (!lifecycleHelper)
                    return;
                // Convert
                var orientation = options?.orientation ?? this.orientation;
                var mirror = options?.mirror ?? image.verticallyMirrored;
                var bufferSize = image.width * image.height * 4;
                EnsureCapacity(ref convertedBuffer, bufferSize);
                EnsureCapacity(ref tempBuffer, bufferSize);
                Convert(in image, orientation, mirror, tempBuffer.GetUnsafePtr(), convertedBuffer.GetUnsafePtr(), out var width, out var height);
                // Update
                this.width = width;
                this.height = height;
                this.timestamp = image.timestamp;
            }
        }

        /// <summary>
        /// Dispose the pixel buffer output and release resources.
        /// </summary>
        public override void Dispose () {
            lock (fence) {
                EnsureCapacity(ref convertedBuffer, 0);
                EnsureCapacity(ref tempBuffer, 0);
                if (lifecycleHelper)
                    lifecycleHelper.Dispose();
            }
        }
        #endregion


        #region --Operations--
        internal readonly LifecycleHelper lifecycleHelper;
        private readonly object fence;
        private NativeArray<byte> convertedBuffer;
        private NativeArray<byte> tempBuffer;
        private static readonly List<RuntimePlatform> OrientationSupport = new List<RuntimePlatform> {
            RuntimePlatform.Android,
            RuntimePlatform.IPhonePlayer
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Convert (
            in CameraImage image,
            ScreenOrientation orientation,
            bool mirror,
            void* tempBuffer,
            void* dstBuffer,
            out int width,
            out int height
        ) {
            var bufferSize = image.width * image.height * 4;
            var planeCount = image.planes?.Length ?? 1;
            var srcBuffers = stackalloc void*[planeCount];
            var rowStrides = stackalloc int[planeCount];
            var pixelStrides = stackalloc int[planeCount];
            if (image.planes != null)
                for (var i = 0; i < planeCount; ++i) {
                    srcBuffers[i] = image.planes[i].buffer.GetUnsafeReadOnlyPtr();
                    rowStrides[i] = image.planes[i].rowStride;
                    pixelStrides[i] = image.planes[i].pixelStride;
                }
            else {
                srcBuffers[0] = image.pixelBuffer.GetUnsafeReadOnlyPtr();
                rowStrides[0] = image.rowStride;
            }
            NatDeviceExt.Convert(
                srcBuffers,
                planeCount,
                image.format,
                image.width,
                image.height,
                rowStrides,
                pixelStrides,
                (int)orientation,
                mirror,
                tempBuffer,
                dstBuffer,
                out width,
                out height
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureCapacity (ref NativeArray<byte> buffer, int capacity) {
            // Check
            if (buffer.Length == capacity)
                return;
            // Dispose // Checking `IsCreated` doesn't prevent this
            try { buffer.Dispose(); } catch (ObjectDisposedException) { }
            // Recreate
            if (capacity > 0)
                buffer = new NativeArray<byte>(capacity, Allocator.Persistent);
        }
        #endregion
    }
}