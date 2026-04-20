using System.Collections.Generic;
using System.Linq;
using Xunit;
using IOStudio.Services.Motion;

namespace IOStudio.Tests.Services
{
    public class SafetyGuardTests
    {
        // ── Clamp ───────────────────────────────────────────

        [Fact]
        public void Check_Clamps_Above_Max()
        {
            var guard = new SafetyGuard();
            float result = guard.Check("ch1", 1.5f, 0.016, minValue: 0f, maxValue: 1f);
            Assert.Equal(1f, result);
        }

        [Fact]
        public void Check_Clamps_Below_Min()
        {
            var guard = new SafetyGuard();
            float result = guard.Check("ch1", -0.5f, 0.016, minValue: 0f, maxValue: 1f);
            Assert.Equal(0f, result);
        }

        [Fact]
        public void Check_InRange_Passes_Through()
        {
            var guard = new SafetyGuard();
            float result = guard.Check("ch1", 0.6f, 0.016, minValue: 0f, maxValue: 1f);
            Assert.Equal(0.6f, result, precision: 4);
        }

        [Fact]
        public void Check_Custom_Range()
        {
            var guard = new SafetyGuard();
            float result = guard.Check("ch1", 5f, 0.016, minValue: 2f, maxValue: 4f);
            Assert.Equal(4f, result);
        }

        // ── Velocity Limiting ───────────────────────────────

        [Fact]
        public void Check_VelocityLimit_Truncates_LargeJump()
        {
            var guard = new SafetyGuard { MaxVelocityPerSecond = 2.0f };
            double dt = 0.016; // ~60fps

            // First call to establish baseline
            guard.Check("ch1", 0f, dt);

            // Second call jumps to 1.0 in 16ms → velocity = 62.5/s >> 2.0/s
            float result = guard.Check("ch1", 1f, dt);

            // Max allowed delta = 2.0 * 0.016 = 0.032
            float expectedMax = 0f + 2.0f * (float)dt;
            Assert.Equal(expectedMax, result, precision: 3);
        }

        [Fact]
        public void Check_VelocityLimit_Allows_SlowChange()
        {
            var guard = new SafetyGuard { MaxVelocityPerSecond = 2.0f };
            double dt = 1.0; // 1 second

            guard.Check("ch1", 0.5f, dt);

            // Change of 0.1 in 1 second → velocity = 0.1/s < 2.0/s
            float result = guard.Check("ch1", 0.6f, dt);
            Assert.Equal(0.6f, result, precision: 4);
        }

        [Fact]
        public void Check_VelocityLimit_Fires_SafetyTriggered()
        {
            var guard = new SafetyGuard { MaxVelocityPerSecond = 1.0f };
            string? message = null;
            guard.SafetyTriggered += msg => message = msg;

            guard.Check("ch1", 0f, 0.016);
            guard.Check("ch1", 1f, 0.016); // huge jump

            Assert.NotNull(message);
            Assert.Contains("ch1", message!);
        }

        [Fact]
        public void Check_No_VelocityLimit_On_First_Call()
        {
            var guard = new SafetyGuard { MaxVelocityPerSecond = 0.001f };
            string? message = null;
            guard.SafetyTriggered += msg => message = msg;

            // First call — no previous value, should not trigger
            float result = guard.Check("ch1", 0.9f, 0.016);
            Assert.Equal(0.9f, result, precision: 4);
            Assert.Null(message);
        }

        [Fact]
        public void Check_IndependentChannels()
        {
            var guard = new SafetyGuard { MaxVelocityPerSecond = 2.0f };

            guard.Check("ch1", 0f, 0.016);
            guard.Check("ch2", 1f, 0.016);

            // ch1 jumps to 1.0 — should be limited
            float r1 = guard.Check("ch1", 1f, 0.016);
            Assert.True(r1 < 0.1f, "ch1 should be velocity-limited");

            // ch2 stays at 1.0 — no velocity change
            float r2 = guard.Check("ch2", 1f, 0.016);
            Assert.Equal(1f, r2, precision: 4);
        }

        // ── Reset ───────────────────────────────────────────

        [Fact]
        public void Reset_ClearsHistory()
        {
            var guard = new SafetyGuard { MaxVelocityPerSecond = 0.001f };

            guard.Check("ch1", 0f, 0.016);

            guard.Reset();

            // After reset, large jump should not be limited (no previous value)
            string? message = null;
            guard.SafetyTriggered += msg => message = msg;

            float result = guard.Check("ch1", 1f, 0.016);
            Assert.Equal(1f, result, precision: 4);
            Assert.Null(message);
        }

        // ── GenerateReturnToCenter ──────────────────────────

        [Fact]
        public void ReturnToCenter_FirstAndLastValues()
        {
            var values = SafetyGuard
                .GenerateReturnToCenter(
                    currentValue: 1.0f,
                    centerValue: 0.5f,
                    durationMs: 100,
                    stepMs: 10
                )
                .ToList();

            // First value should be close to start (but not equal — step 1 of N)
            Assert.True(values.First() > 0.5f, "First value should be closer to start");

            // Last value should be at center
            Assert.Equal(0.5f, values.Last(), precision: 3);
        }

        [Fact]
        public void ReturnToCenter_StepCount()
        {
            var values = SafetyGuard
                .GenerateReturnToCenter(
                    currentValue: 1.0f,
                    centerValue: 0.5f,
                    durationMs: 160,
                    stepMs: 16
                )
                .ToList();

            Assert.Equal(10, values.Count); // 160 / 16 = 10 steps
        }

        [Fact]
        public void ReturnToCenter_Smoothstep_Monotonic()
        {
            var values = SafetyGuard
                .GenerateReturnToCenter(
                    currentValue: 1.0f,
                    centerValue: 0f,
                    durationMs: 500,
                    stepMs: 10
                )
                .ToList();

            // Values should monotonically decrease from ~1.0 toward 0
            for (int i = 1; i < values.Count; i++)
            {
                Assert.True(
                    values[i] <= values[i - 1],
                    $"Not monotonic at step {i}: {values[i]} > {values[i - 1]}"
                );
            }
        }
    }
}
