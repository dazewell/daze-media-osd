using DazeMediaOsd.Core.Audio;

namespace DazeMediaOsd.Core.Tests;

public class DeviceKindTests
{
    // Values as read on 26300 (2026-10-23): laptop speakers 1, Bluetooth/jack headphones 3, mic array 4,
    // Bluetooth hands-free 5, Dell monitor over DisplayPort 9, Stereo Mix 10.
    [Theory]
    [InlineData(0u, DeviceKind.Other)]       // RemoteNetworkDevice
    [InlineData(1u, DeviceKind.Speakers)]
    [InlineData(2u, DeviceKind.Other)]       // LineLevel
    [InlineData(3u, DeviceKind.Headphones)]
    [InlineData(4u, DeviceKind.Microphone)]
    [InlineData(5u, DeviceKind.Headset)]
    [InlineData(6u, DeviceKind.Headset)]     // Handset
    [InlineData(8u, DeviceKind.Other)]       // SPDIF
    [InlineData(9u, DeviceKind.Display)]
    [InlineData(10u, DeviceKind.Other)]      // UnknownFormFactor
    [InlineData(99u, DeviceKind.Other)]
    public void FormFactorMapsToKind(uint formFactor, DeviceKind kind) =>
        Assert.Equal(kind, DeviceKinds.FromFormFactor(formFactor));
}
