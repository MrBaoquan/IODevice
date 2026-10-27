# Output SlewRate Contract

## Default behavior

`SetDO` keeps the existing call shape. For each physical Key:

- `SlewRate <= 0`: write immediately, matching legacy behavior.
- `SlewRate > 0` with valid `Min`/`Max`: rate-limit the command toward the target.
- `SlewRate > 0` without valid `Min`/`Max`: log a warning once and fall back to immediate output.

`SlewRate` is expressed in normalized range units per second. `Min` and `Max` are the
physical Key's engineering range. Rate limiting is applied in the command buffer and is
advanced when `DOImmediate()`/`FlushDO()` flushes output.

## Explicit immediate mode

Use immediate mode for emergency stop, reset/safe state, pulse edges, and explicit
commissioning overrides.

C++:

```cpp
SetDO(key, value, EOutputWriteMode::Immediate);
SetDOImmediate(key, value);
SetDO(action, value, EOutputWriteMode::Immediate);
SetDOImmediate(action, value);
```

C ABI:

```c
SetDOSingleEx(device, key, value, 1);
SetDOSingleImmediate(device, key, value);
SetDOActionEx(device, action, value, 1, false);
SetDOActionImmediate(device, action, value, false);
SetDOAllEx(device, values, 1);
```

C#:

```csharp
device.SetDO(key, value, OutputWriteMode.Immediate);
device.SetDOImmediate(key, value);
device.SetDO(action, value, OutputWriteMode.Immediate);
device.SetDOImmediate(action, value);
```

## OAction mappings

An OAction may map to multiple physical Keys. Each Key is limited independently using
its own `SlewRate`, `Min`, and `Max`.

## MotionPlayer

MotionPlayer keeps its existing rate-limited diagnostics. Because it already computes
the limited value, it writes with `EOutputWriteMode::Immediate` to IODevice so the same
command is not rate-limited twice. Direct/debug writes use IODevice's configured mode.

## IOStudio output debug

- Normal switch/analog/step writes use configured mode.
- Reset, emergency stop, and pulse edges use immediate mode.
- The output window sends and reads engineering values; its UI continues to display and
  accept normalized values mapped through the selected channel range.


## SafeStop channel policy

`SlewRate` only limits normal content output. SafeStop uses separate channel policy:

- `SafeValue`: engineering-unit target used by SafeStop.
- `SafeStopPolicy=Controlled`: ramp toward `SafeValue` using `SafeStopRate`.
- `SafeStopPolicy=Immediate`: write `SafeValue` immediately. Use this for discrete outputs and true hard stops.
- `SafeStopRate`: normalized range units per second. When omitted/zero, a Controlled policy falls back to `SlewRate`; if neither exists, the channel is treated as Immediate.

Recommended YDL configuration:

```xml
<Properties>
  <Key Name="OAxis_06" Min="0" Max="1000" SlewRate="0.5"
       SafeValue="0" SafeStopRate="0.5" SafeStopPolicy="Controlled" />
  <Key Name="OAxis_16" SafeValue="0" SafeStopPolicy="Immediate" />
</Properties>
```

Rules:

- Never use a hardcoded midpoint such as `0.5` as the generic safe value.
- Boolean/discrete channels must resolve to `SafeValue=0/false`.
- A Controlled channel must reach `SafeValue` before SafeStop is reported settled.
- Emergency stop may use Immediate mode, but that is a distinct safety policy from a controlled return-to-idle.
