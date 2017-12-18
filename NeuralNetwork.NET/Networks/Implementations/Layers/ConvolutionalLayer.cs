﻿using JetBrains.Annotations;
using NeuralNetworkNET.APIs.Interfaces;
using NeuralNetworkNET.APIs.Misc;
using NeuralNetworkNET.Extensions;
using NeuralNetworkNET.Networks.Activations;
using NeuralNetworkNET.Networks.Activations.Delegates;
using NeuralNetworkNET.Networks.Implementations.Layers.Abstract;
using NeuralNetworkNET.Networks.Implementations.Layers.Helpers;
using NeuralNetworkNET.Structs;
using Newtonsoft.Json;

namespace NeuralNetworkNET.Networks.Implementations.Layers
{
    /// <summary>
    /// A convolutional layer, used in a CNN network
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    internal class ConvolutionalLayer : WeightedLayerBase, INetworkLayer3D
    {
        #region Parameters

        /// <inheritdoc/>
        public override LayerType LayerType { get; } = LayerType.Convolutional;

        /// <inheritdoc/>
        public override int Inputs => InputInfo.Size;

        /// <inheritdoc/>
        public override int Outputs => OutputInfo.Size;

        /// <inheritdoc/>
        [JsonProperty(nameof(InputInfo), Order = 4)]
        public TensorInfo InputInfo { get; }

        /// <summary>
        /// Gets the <see cref="TensorInfo"/> associated with each kernel in the layer
        /// </summary>
        [JsonProperty(nameof(KernelInfo), Order = 5)]
        public TensorInfo KernelInfo { get; }

        /// <summary>
        /// Gets the number of kernels in the current layer
        /// </summary>
        [JsonProperty(nameof(Kernels), Order = 6)]
        public int Kernels => Weights.GetLength(0);

        /// <inheritdoc/>
        [JsonProperty(nameof(OutputInfo), Order = 7)]
        public TensorInfo OutputInfo { get; }

        #endregion

        public ConvolutionalLayer(TensorInfo input, (int X, int Y) kernelSize, int kernels, ActivationFunctionType activation)
            : base(WeightsProvider.ConvolutionalKernels(input.Channels, kernelSize.X, kernelSize.Y, kernels),
                  WeightsProvider.Biases(kernels), activation)
        {
            InputInfo = input;
            KernelInfo = new TensorInfo(kernelSize.X, kernelSize.Y, input.Channels);
            OutputInfo = new TensorInfo(input.Height - kernelSize.X + 1, input.Width - kernelSize.Y + 1, kernels);
        }

        public ConvolutionalLayer(TensorInfo input, TensorInfo kernels, TensorInfo output,
            [NotNull] float[,] weights, [NotNull] float[] biases, ActivationFunctionType activation)
            : base(weights, biases, activation)
        {
            InputInfo = input;
            KernelInfo = kernels;
            OutputInfo = output;
        }

        /// <inheritdoc/>
        public override unsafe void Forward(in Tensor x, out Tensor z, out Tensor a)
        {
            x.ConvoluteForward(InputInfo, Weights, KernelInfo, Biases, out z);
            if (ActivationFunctionType == ActivationFunctionType.Identity) Tensor.From(z, z.Entities, z.Length, out a);
            else z.Activation(ActivationFunctions.Activation, out a);
        }

        /// <inheritdoc/>
        public override unsafe void Backpropagate(in Tensor delta_1, in Tensor z, ActivationFunction activationPrime)
        {
            fixed (float* pw = Weights)
            {
                Tensor.Fix(pw, Weights.GetLength(0), Weights.GetLength(1), out Tensor weights);
                weights.Rotate180(KernelInfo.Channels, out Tensor w180);
                delta_1.ConvoluteBackwards(OutputInfo, w180, KernelInfo, out Tensor delta);
                w180.Free();
                z.InPlaceActivationAndHadamardProduct(delta, activationPrime);
                delta.Free();
            }
        }

        /// <inheritdoc/>
        public override void ComputeGradient(in Tensor a, in Tensor delta, out Tensor dJdw, out Tensor dJdb)
        {
            a.Rotate180(InputInfo.Channels, out Tensor a180);
            a180.ConvoluteGradient(InputInfo, delta, OutputInfo, out dJdw);
            a180.Free();
            delta.CompressVertically(out dJdb);
        }

        /// <inheritdoc/>
        public override INetworkLayer Clone() => new ConvolutionalLayer(InputInfo, KernelInfo, OutputInfo, Weights.BlockCopy(), Biases.BlockCopy(), ActivationFunctionType);
    }
}
