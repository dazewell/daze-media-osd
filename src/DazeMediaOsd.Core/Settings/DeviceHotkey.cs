using DazeMediaOsd.Core.Audio;

namespace DazeMediaOsd.Core.Settings;

/// <summary>
/// A shortcut that makes one device the default (D-022). <paramref name="Id"/> is the endpoint id ("" until
/// a device is picked); <paramref name="Name"/> its last-known friendly name, so Settings and the OSD can
/// still name it while it is unplugged. <paramref name="Hotkey"/> as in <see cref="AppSettings.CycleOutputHotkey"/>; "" is none.
/// </summary>
public sealed record DeviceHotkey(string Id = "", EndpointFlow Flow = EndpointFlow.Output, string Name = "", string Hotkey = "");
