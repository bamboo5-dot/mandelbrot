﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace ConsoleApp
{
    public static class Program
    {
        static readonly int Height = 1024;
        static readonly int Width = 1024;
        static readonly float MinX = -2.0f;
        static readonly float MaxX = 0.47f;
        static readonly float MinY = -1.12f;
        static readonly float MaxY = 1.12f;
        static readonly float ScaleX = (MaxX - MinX) / Width;
        static readonly float ScaleY = (MaxY - MinY) / Height;
        static readonly int MaxIters = 256;
        static readonly int NumCpu = Environment.ProcessorCount;
        static readonly Vector256<float> MinYVec = Vector256.Create(MinY);
        static readonly Vector256<float> ScaleXVec = Vector256.Create(ScaleX);
        static readonly Vector256<float> ScaleYVec = Vector256.Create(ScaleY);
        static readonly Vector256<uint> IdentVector = Vector256.Create<uint>(1);
        static readonly Vector256<float> FourVec = Vector256.Create(4.0f);
        private static readonly Vector256<float> TwoVec = Vector256.Create(2f);
        private static readonly float[] DisplacementArray = { 0, 1, 2, 3, 4, 5, 6, 7 };
        private static readonly Vector256<float> DisplacementVector = Vector256.Create(DisplacementArray);
        private static readonly uint[] Result = new uint[Height * Width];
        private static unsafe volatile uint* _pResult;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MandelbrotSimd()
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = NumCpu
            };
            Parallel.For(0, Height / 2   , options, ComplexPlaneSimd);
        }

        private static void ComplexPlaneSimd(int h)
        {
            unchecked
            {
                int offset = h * Width;
                int mirrorOffset = (Height - h - 1) * Width;
                for (int w = 0; w < Width; w += Vector256<float>.Count)
                {
                    var cyVec = Avx.Add(MinYVec, Avx.Multiply(Vector256.Create((float)h), ScaleYVec));
                    var cxVec = Avx.Add(Vector256.Create(MinX + w * ScaleX),
                        Avx.Multiply(DisplacementVector, ScaleXVec));
                    Mandelbrot_0_simd(offset + w, mirrorOffset + w, cxVec, cyVec);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void Mandelbrot_0_simd(int offset, int mirrorOffset, Vector256<float> cReVec, Vector256<float> cImVec)
        {
            unchecked
            {
                var zReVec = Vector256<float>.Zero;
                var zImVec = Vector256<float>.Zero;
                var nvVec = Vector256<uint>.Zero;
                var breakVec = Vector256.Create<uint>(0);
                for (int i = 1; i < MaxIters; i++)
                {
                    var zReNewVec = Avx.Add(Avx.Subtract(Avx.Multiply(zReVec, zReVec), Avx.Multiply(zImVec, zImVec)),
                        cReVec);
                    var zImNewVec = Avx.Add(Avx.Multiply(Avx.Multiply(zReVec, zImVec), TwoVec), cImVec);
                    var mag2Vec = Avx.Add(Avx.Multiply(zReNewVec, zReNewVec), Avx.Multiply(zImNewVec, zImNewVec));
                    var maskVec = Avx2.Add(Avx.CompareLessThan(mag2Vec, FourVec).AsUInt32(), IdentVector);
                    breakVec = Avx2.Or(maskVec, breakVec);
                    nvVec = Avx2.Add(nvVec,
                        Avx2.AndNot(maskVec, IdentVector));
                    if (Avx.TestZ(Avx2.AndNot(breakVec, IdentVector), IdentVector))
                    {
                        break;
                    }
                    zReVec = zReNewVec;
                    zImVec = zImNewVec;
                }
                Avx.Store(_pResult + offset, nvVec);
                Avx.Store(_pResult + mirrorOffset , nvVec);
            }
        }

        public static unsafe void Main()
        {
            Console.WriteLine("NumCpu : {0}", NumCpu);
            Console.WriteLine("Avx.IsSupported : {0}", Avx.IsSupported);
            Console.WriteLine("Avx2.IsSupported : {0}", Avx2.IsSupported);
            Console.WriteLine("Vector256.IsHardwareAccelerated : {0}", Vector256.IsHardwareAccelerated);
            fixed (uint* pr = Result)
            {
                _pResult = pr;

                var measurements = new List<double>();
                for (int i = -1; i < 10; i++)
                {
                    Console.Write(i + 1 + "\t ");
                    Console.Out.Flush();
                    Array.Clear(Result);
                    var stopWatch = new Stopwatch();
                    stopWatch.Start();
                    MandelbrotSimd();
                    stopWatch.Stop();
                    var executionTime = stopWatch.Elapsed;
                    if (i >= 0)
                    {
                        measurements.Add(executionTime.TotalMilliseconds);
                    }

                    var sum = Result.Aggregate(0u, (s, u) => s + u);
                    Console.WriteLine("Execution Time:      {0:F2}ms\t  {1}", executionTime.TotalMilliseconds, sum);
                }

                var average = measurements.Average();
                var sumOfSquares = measurements.Select(x => Math.Pow(x - average, 2)).Sum();
                var standardDeviation = Math.Sqrt(sumOfSquares / (measurements.Count - 1)) / average * 100;
                Console.WriteLine("Avg: {0:F2}ms, StdDev: {1:F2}%", average, standardDeviation);
            }
            string[] resultStrings = Result.Select(x => x.ToString()).ToArray();
            string resultFile = string.Join(",", resultStrings);
            string filePath = "output.txt";
            File.WriteAllText(filePath, resultFile);
        }
    }
}