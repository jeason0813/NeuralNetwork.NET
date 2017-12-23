﻿using Alea;
using Alea.cuDNN;
using JetBrains.Annotations;
using NeuralNetworkNET.Cuda.Services;
using NeuralNetworkNET.Cuda.Extensions;
using NeuralNetworkNET.Extensions;
using NeuralNetworkNET.Networks.Activations;
using NeuralNetworkNET.Networks.Activations.Delegates;
using NeuralNetworkNET.Networks.Implementations.Layers;
using NeuralNetworkNET.APIs.Structs;
using NeuralNetworkNET.APIs.Enums;
using NeuralNetworkNET.APIs.Interfaces;

namespace NeuralNetworkNET.Cuda.Layers
{
    internal class CuDnnFullyConnectedLayer : FullyConnectedLayer
    {
        /// <summary>
        /// Gets the <see cref="Dnn"/> instance for the current layer
        /// </summary>
        [NotNull]
        private readonly Dnn DnnInstance = DnnService.Instance;

        public CuDnnFullyConnectedLayer(in TensorInfo input, int neurons, ActivationFunctionType activation, WeightsInitializationMode weightsMode, BiasInitializationMode biasMode) 
            : base(input, neurons, activation, weightsMode, biasMode) { }

        public CuDnnFullyConnectedLayer(in TensorInfo input, int neurons, [NotNull] float[] weights, [NotNull] float[] biases, ActivationFunctionType activation) 
            : base(input, neurons, weights, biases, activation) { }

        /// <inheritdoc/>
        public override unsafe void Forward(in Tensor x, out Tensor z, out Tensor a)
        {
            fixed (float* pw = Weights)
            {
                Tensor.Reshape(pw, InputInfo.Size, OutputInfo.Size, out Tensor wTensor);
                using (DeviceMemory<float>
                    x_gpu = DnnInstance.Gpu.AllocateDevice(x),
                    w_gpu = DnnInstance.Gpu.AllocateDevice(wTensor),
                    y_gpu = DnnInstance.Gpu.AllocateDevice<float>(x.Entities * OutputInfo.Size),
                    b_gpu = DnnInstance.Gpu.AllocateDevice(Biases))
                {
                    DnnInstance.FullyConnectedForward(x.Entities, x.Length, OutputInfo.Size, x_gpu.Ptr, w_gpu.Ptr, b_gpu.Ptr, y_gpu.Ptr);
                    y_gpu.CopyToHost(x.Entities, OutputInfo.Size, out z);
                    DnnInstance.ActivationForward(z.Entities, z.Length, y_gpu.Ptr, y_gpu.Ptr, ActivationFunctions.Activation);
                    y_gpu.CopyToHost(z.Entities, z.Length, out a);
                }
            }
        }

        /// <inheritdoc/>
        public override unsafe void Backpropagate(in Tensor delta_1, in Tensor z, ActivationFunction activationPrime)
        {
            fixed (float* pw = Weights)
            {
                Tensor.Reshape(pw, InputInfo.Size, OutputInfo.Size, out Tensor wTensor);
                using (DeviceMemory<float>
                    delta_1_gpu = DnnInstance.Gpu.AllocateDevice(delta_1),
                    w_gpu = DnnInstance.Gpu.AllocateDevice(wTensor),
                    z_gpu = DnnInstance.Gpu.AllocateDevice(z))
                {
                    DnnInstance.FullyConnectedBackwardData(z.Entities, InputInfo.Size, OutputInfo.Size, z_gpu.Ptr, delta_1_gpu.Ptr, w_gpu.Ptr, activationPrime);
                    z_gpu.CopyTo(z);
                }
            }
        }

        /// <inheritdoc/>
        public override void ComputeGradient(in Tensor a, in Tensor delta, out Tensor dJdw, out Tensor dJdb)
        {
            using (DeviceMemory<float>
                a_gpu = DnnInstance.Gpu.AllocateDevice(a),
                delta_gpu = DnnInstance.Gpu.AllocateDevice(delta),
                w_gpu = DnnInstance.Gpu.AllocateDevice<float>(a.Length * delta.Length))
            {
                DnnInstance.FullyConnectedBackwardFilter(a.Entities, a.Length, delta.Length, a_gpu.Ptr, delta_gpu.Ptr, w_gpu.Ptr);
                w_gpu.CopyToHost(a.Length, delta.Length, out dJdw);
            }
            delta.CompressVertically(out dJdb); // Doing this on CPU is generally faster than launching the kernels
        }

        /// <summary>
        /// Tries to deserialize a new <see cref="CuDnnFullyConnectedLayer"/> from the input <see cref="System.IO.Stream"/>
        /// </summary>
        /// <param name="stream">The input <see cref="System.IO.Stream"/> to use to read the layer data</param>
        [MustUseReturnValue, CanBeNull]
        public new static INetworkLayer Deserialize([NotNull] System.IO.Stream stream)
        {
            if (!stream.TryRead(out TensorInfo input)) return null;
            if (!stream.TryRead(out TensorInfo output)) return null;
            if (!stream.TryRead(out ActivationFunctionType activation)) return null;
            if (!stream.TryRead(out int wLength)) return null;
            float[] weights = stream.ReadUnshuffled(wLength);
            if (!stream.TryRead(out int bLength)) return null;
            float[] biases = stream.ReadUnshuffled(bLength);
            return new CuDnnFullyConnectedLayer(input, output.Size, weights, biases, activation);
        }
    }
}
