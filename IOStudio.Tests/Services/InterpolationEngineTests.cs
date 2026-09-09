using System.Collections.Generic;
using Xunit;
using IOStudio.Models.Motion;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    public class InterpolationEngineTests
    {
        // ── Linear ──────────────────────────────────────────

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(1f, 10f)]
        [InlineData(0.5f, 5f)]
        [InlineData(0.25f, 2.5f)]
        public void Linear_Interpolates_Correctly(float t, float expected)
        {
            float result = InterpolationEngine.Linear(0f, 10f, t);
            Assert.Equal(expected, result, precision: 4);
        }

        [Fact]
        public void Linear_SameValues_Returns_Constant()
        {
            Assert.Equal(5f, InterpolationEngine.Linear(5f, 5f, 0.5f), precision: 4);
        }

        // ── Step ────────────────────────────────────────────

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(0.5f, 0f)]
        [InlineData(0.999f, 0f)]
        [InlineData(1f, 1f)]
        public void Step_Holds_Previous_Until_End(float t, float expected)
        {
            float result = InterpolationEngine.Step(0f, 1f, t);
            Assert.Equal(expected, result, precision: 4);
        }

        // ── EaseInOut ───────────────────────────────────────

        [Fact]
        public void EaseInOut_At_Endpoints()
        {
            Assert.Equal(0f, InterpolationEngine.EaseInOut(0f, 1f, 0f), precision: 4);
            Assert.Equal(1f, InterpolationEngine.EaseInOut(0f, 1f, 1f), precision: 4);
        }

        [Fact]
        public void EaseInOut_Midpoint_Is_Half()
        {
            // smoothstep(0.5) = 0.5²*(3 - 2*0.5) = 0.25*2 = 0.5
            Assert.Equal(0.5f, InterpolationEngine.EaseInOut(0f, 1f, 0.5f), precision: 4);
        }

        [Fact]
        public void EaseInOut_Near_Zero_Slope()
        {
            // smoothstep has ~zero derivative at t=0 and t=1
            float nearStart = InterpolationEngine.EaseInOut(0f, 1f, 0.01f);
            float nearEnd = InterpolationEngine.EaseInOut(0f, 1f, 0.99f);

            // Near start: value should be very close to 0
            Assert.True(nearStart < 0.001f, $"nearStart={nearStart} should be ~0");
            // Near end: value should be very close to 1
            Assert.True(nearEnd > 0.999f, $"nearEnd={nearEnd} should be ~1");
        }

        // ── Bezier ──────────────────────────────────────────

        [Fact]
        public void Bezier_EvenlySpacedControlPoints_Degenerates_To_Linear()
        {
            // When tangentOut=1 and tangentIn=1, control points are evenly spaced:
            // P1 = a + 1*|span|/3, P2 = b - 1*|span|/3 → linear
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                float bezier = InterpolationEngine.Bezier(0f, 1f, t, 1f, 1f);
                float linear = InterpolationEngine.Linear(0f, 1f, t);
                Assert.Equal(linear, bezier, precision: 3);
            }
        }

        [Fact]
        public void Bezier_Endpoints()
        {
            Assert.Equal(0f, InterpolationEngine.Bezier(0f, 1f, 0f, 0.5f, 0.5f), precision: 4);
            Assert.Equal(1f, InterpolationEngine.Bezier(0f, 1f, 1f, 0.5f, 0.5f), precision: 4);
        }

        // ── Evaluate ────────────────────────────────────────

        [Fact]
        public void Evaluate_EmptyList_Returns_Default()
        {
            float result = InterpolationEngine.Evaluate(new List<MotionKeyframe>(), 500);
            Assert.Equal(0.5f, result);
        }

        [Fact]
        public void Evaluate_NullList_Returns_Default()
        {
            float result = InterpolationEngine.Evaluate(null!, 500);
            Assert.Equal(0.5f, result);
        }

        [Fact]
        public void Evaluate_BeforeFirstKeyframe_Returns_FirstValue()
        {
            var keyframes = new List<MotionKeyframe>
            {
                new() { TimeMs = 100, Value = 0.3f },
                new() { TimeMs = 200, Value = 0.7f }
            };

            Assert.Equal(0.3f, InterpolationEngine.Evaluate(keyframes, 0));
            Assert.Equal(0.3f, InterpolationEngine.Evaluate(keyframes, 50));
            Assert.Equal(0.3f, InterpolationEngine.Evaluate(keyframes, 100)); // exactly at first
        }

        [Fact]
        public void Evaluate_AfterLastKeyframe_Returns_LastValue()
        {
            var keyframes = new List<MotionKeyframe>
            {
                new() { TimeMs = 100, Value = 0.3f },
                new() { TimeMs = 200, Value = 0.7f }
            };

            Assert.Equal(0.7f, InterpolationEngine.Evaluate(keyframes, 200));
            Assert.Equal(0.7f, InterpolationEngine.Evaluate(keyframes, 999));
        }

        [Fact]
        public void Evaluate_Linear_Midpoint()
        {
            var keyframes = new List<MotionKeyframe>
            {
                new()
                {
                    TimeMs = 0,
                    Value = 0f,
                    Interpolation = "linear"
                },
                new() { TimeMs = 1000, Value = 1f }
            };

            float result = InterpolationEngine.Evaluate(keyframes, 500);
            Assert.Equal(0.5f, result, precision: 3);
        }

        [Fact]
        public void Evaluate_Step_Before_End()
        {
            var keyframes = new List<MotionKeyframe>
            {
                new()
                {
                    TimeMs = 0,
                    Value = 0f,
                    Interpolation = "step"
                },
                new() { TimeMs = 1000, Value = 1f }
            };

            Assert.Equal(0f, InterpolationEngine.Evaluate(keyframes, 500));
        }

        [Fact]
        public void Evaluate_EaseInOut_Alias()
        {
            var keyframes = new List<MotionKeyframe>
            {
                new()
                {
                    TimeMs = 0,
                    Value = 0f,
                    Interpolation = "ease"
                },
                new() { TimeMs = 1000, Value = 1f }
            };

            float result = InterpolationEngine.Evaluate(keyframes, 500);
            // smoothstep midpoint = 0.5
            Assert.Equal(0.5f, result, precision: 3);
        }

        [Fact]
        public void Evaluate_MultipleKeyframes_BinarySearch()
        {
            var keyframes = new List<MotionKeyframe>
            {
                new()
                {
                    TimeMs = 0,
                    Value = 0f,
                    Interpolation = "linear"
                },
                new()
                {
                    TimeMs = 100,
                    Value = 0.2f,
                    Interpolation = "linear"
                },
                new()
                {
                    TimeMs = 200,
                    Value = 0.8f,
                    Interpolation = "linear"
                },
                new()
                {
                    TimeMs = 300,
                    Value = 0.4f,
                    Interpolation = "linear"
                },
                new() { TimeMs = 400, Value = 1.0f }
            };

            // Between keyframe[1] (0.2 @ 100) and keyframe[2] (0.8 @ 200), midpoint
            float result = InterpolationEngine.Evaluate(keyframes, 150);
            Assert.Equal(0.5f, result, precision: 3);

            // Between keyframe[2] (0.8 @ 200) and keyframe[3] (0.4 @ 300), midpoint
            result = InterpolationEngine.Evaluate(keyframes, 250);
            Assert.Equal(0.6f, result, precision: 3);
        }

        [Fact]
        public void Evaluate_SingleKeyframe_Always_Returns_Its_Value()
        {
            var keyframes = new List<MotionKeyframe>
            {
                new() { TimeMs = 500, Value = 0.42f }
            };

            Assert.Equal(0.42f, InterpolationEngine.Evaluate(keyframes, 0));
            Assert.Equal(0.42f, InterpolationEngine.Evaluate(keyframes, 500));
            Assert.Equal(0.42f, InterpolationEngine.Evaluate(keyframes, 1000));
        }

        // ── EvaluateClip ────────────────────────────────────

        [Fact]
        public void EvaluateClip_Converts_AbsoluteTime_To_Local()
        {
            var clip = new MotionClip
            {
                StartMs = 1000,
                EndMs = 2000,
                Keyframes = new List<MotionKeyframe>
                {
                    new()
                    {
                        TimeMs = 0,
                        Value = 0f,
                        Interpolation = "linear"
                    },
                    new() { TimeMs = 1000, Value = 1f }
                }
            };

            // absoluteTime=1500 → localTime=500 → midpoint of [0..1000] → 0.5
            float result = InterpolationEngine.EvaluateClip(clip, 1500);
            Assert.Equal(0.5f, result, precision: 3);
        }

        [Fact]
        public void EvaluateClip_NullClip_Returns_Default()
        {
            Assert.Equal(0.5f, InterpolationEngine.EvaluateClip(null!, 100));
        }

        [Fact]
        public void EvaluateClip_EmptyKeyframes_Returns_Default()
        {
            var clip = new MotionClip
            {
                StartMs = 0,
                EndMs = 1000,
                Keyframes = new List<MotionKeyframe>()
            };

            Assert.Equal(0.5f, InterpolationEngine.EvaluateClip(clip, 500));
        }

        // ── Bezier Cross-Engine Consistency ─────────────────
        // C++ uses: P0=k0.value, P1=k0.value + cp2y*|span|/3, P2=k1.value - cp1y*|span|/3, P3=k1.value
        // C# uses:  P0=a, P1=a + tangentOut*|span|/3, P2=b - tangentIn*|span|/3, P3=b
        // Where cp2y maps to TangentOut and cp1y maps to TangentIn.
        // This test verifies the semantics are aligned.

        [Fact]
        public void Bezier_CppCsharp_Semantics_Match()
        {
            // Simulate C++ logic with the SAME values C# would use
            float k0Value = 0.2f;
            float k1Value = 0.8f;
            float cp2y = 1.5f; // tangentOut of k0 (stored as cp2y in JSON)
            float cp1y = 0.5f; // tangentIn of k1 (stored as cp1y in JSON)

            float span = k1Value - k0Value; // 0.6
            float absSpan = MathF.Abs(span);

            // C++ control points (after our fix)
            float cppP1 = k0Value + cp2y * absSpan / 3.0f;
            float cppP2 = k1Value - cp1y * absSpan / 3.0f;

            // C# control points (InterpolationEngine.Bezier)
            // P1 = a + tangentOut * |span| / 3, where tangentOut = cp2y
            // P2 = b - tangentIn * |span| / 3, where tangentIn = cp1y
            float csharpP1 = k0Value + cp2y * absSpan / 3.0f;
            float csharpP2 = k1Value - cp1y * absSpan / 3.0f;

            Assert.Equal(cppP1, csharpP1, precision: 6);
            Assert.Equal(cppP2, csharpP2, precision: 6);

            // Verify actual interpolation matches across multiple t values
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                float csharpResult = InterpolationEngine.Bezier(k0Value, k1Value, t, cp2y, cp1y);

                // Manual C++ formula: (1-t)³*P0 + 3*(1-t)²*t*P1 + 3*(1-t)*t²*P2 + t³*P3
                float u = 1f - t;
                float cppResult =
                    u * u * u * k0Value
                    + 3f * u * u * t * cppP1
                    + 3f * u * t * t * cppP2
                    + t * t * t * k1Value;

                Assert.Equal(cppResult, csharpResult, precision: 4);
            }
        }

        [Fact]
        public void Bezier_ZeroTangents_Starts_Flat()
        {
            // tangentOut=0, tangentIn=0 → P1=a, P2=b → flat start and end
            float result = InterpolationEngine.Bezier(0f, 1f, 0.5f, 0f, 0f);
            Assert.Equal(0.5f, result, precision: 3);
        }
    }
}
