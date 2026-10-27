using System.Text.Json;
using NexaAsset.Mcp;
using NexaModern;

if (args.Length > 0 && args[0] == "--live") { await GatewayTests.Live(); return; }
string root = args.Length == 0 ? Path.Combine(Path.GetTempPath(), "nexa-studio-tests-" + Guid.NewGuid().ToString("N")) : Path.GetFullPath(args[0]);
Directory.CreateDirectory(root);
byte[] rgba = new byte[4096];
rgba[0] = 231; rgba[1] = 19; rgba[2] = 87; rgba[3] = 0;
rgba[4] = 13; rgba[5] = 97; rgba[6] = 241; rgba[7] = 127;
rgba[8] = 240; rgba[9] = 100; rgba[10] = 11; rgba[11] = 255;
Check(ArtDocument.FromArgb(ArtDocument.ToArgb(rgba)).AsSpan().SequenceEqual(rgba), "RGBA/ARGB exact roundtrip");
Check(NativePng.Read(Png.Encode(new(32, 32, rgba))).Rgba.AsSpan().SequenceEqual(rgba), "PNG exact hidden RGB/partial alpha");
foreach (int w in new[] { 32, 64 }) foreach (int h in new[] { 32, 64 }) ArtDocument.Create("A", 4, w, h);
Reject(() => ArtDocument.Create("A", 4, 33, 32), "unsupported sprite shape");
var doc = ArtDocument.Create("A", 4, 32, 32, rgba);
Check(ArtDocument.Composite(doc.State, 0).AsSpan().SequenceEqual(rgba), "one-layer hidden RGB composition");
doc.Edit(0, s => s.Frames[0].Layers.Add(new() { Name = "Overlay", Rgba = new byte[4096] }));
Check(ArtDocument.Composite(doc.State, 0).AsSpan().SequenceEqual(rgba), "Empty overlay erased hidden RGB");
doc.Edit(1, s => ArtDocument.SetPixels(s, 0, 1, J(new[] { new { x = 0, y = 0, rgba = new[] { 10, 20, 30, 128 } } })));
var composed = ArtDocument.Composite(doc.State, 0); Check(composed[0] == 10 && composed[3] == 128, "straight alpha composition");
doc.History(2, false); Check(doc.State.Frames[0].Layers[1].Rgba[3] == 0, "undo pixels");
doc.History(3, true); Check(doc.State.Frames[0].Layers[1].Rgba[3] == 128, "redo pixels");
Reject(() => doc.Edit(3, _ => { }), "stale document revision");
byte[] before = doc.Sidecar();
Reject(() => doc.Edit(4, s => ArtDocument.SetPixels(s, 0, 1, J(new[] { new { x = 1, y = 0, rgba = new[] { 0, 0, 0, 255 } }, new { x = 99, y = 0, rgba = new[] { 0, 0, 0, 255 } } }))), "atomic pixel batch");
Check(doc.Sidecar().AsSpan().SequenceEqual(before), "invalid batch changed draft");
doc.Edit(4, s => ArtDocument.Transform(s, 0, 1, "flipX", 0, 0));
Check(doc.State.Frames[0].Layers[1].Rgba[31 * 4 + 3] == 128, "flip exact pixels");
var restored = ArtDocument.Create("A", 4, 32, 32); restored.Load(0, doc.Sidecar());
Check(restored.Sidecar().AsSpan().SequenceEqual(doc.Sidecar()), "sidecar roundtrip");
Console.WriteLine("PASS: RGBA/ARGB, hidden RGB/partial alpha PNG, four geometries, layers, history, conflicts, atomic pixel batch, transform and sidecar");
NativeArtTests.Run(root);
await ProviderTests.Run();
await StudioRegressionTests.Run();
await GatewayTests.Run();
Console.WriteLine("PASS: all Studio tests");

static JsonElement J(object value) => JsonSerializer.SerializeToElement(value, ArtDocument.Json);
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or FormatException or JsonException) { return; } throw new Exception("Accepted invalid input: " + name); }
