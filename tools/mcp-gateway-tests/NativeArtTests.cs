using System.Security.Cryptography;
using System.Text.Json;
using NexaAsset.Mcp;
using NexaModern;

internal static class NativeArtTests
{
    public static void Run(string root)
    {
        string nativeRoot = Path.Combine(root, "native-source"), session = Path.Combine(root, "helper-session");
        Directory.CreateDirectory(nativeRoot); Directory.CreateDirectory(session);
        byte[] info = Enumerable.Range(1, 4).SelectMany(n => NativeWorkspace.Scalar(n, 1)).Concat(NativeWorkspace.Scalar(5, 1)).ToArray();
        byte[] original = NativeWorkspace.Scalar(1, 100).Concat(NativeWorkspace.Blob(2, NativeWorkspace.Scalar(1, 2).Concat(NativeWorkspace.Blob(3, info)).ToArray())).Concat(NativeWorkspace.Blob(99, new byte[] { 77 })).ToArray();
        File.WriteAllBytes(Path.Combine(nativeRoot, "appearances.dat"), NativeWorkspace.Blob(1, original));
        File.WriteAllBytes(Path.Combine(nativeRoot, "sheet.lzma"), NativeSave.EncodeSheet(new(384, 384, new byte[384 * 384 * 4])));
        File.WriteAllText(Path.Combine(nativeRoot, "catalog-content.json"), "[{\"type\":\"appearances\",\"file\":\"appearances.dat\"},{\"type\":\"sprite\",\"file\":\"sheet.lzma\",\"spritetype\":0,\"firstspriteid\":1,\"lastspriteid\":3}]");
        var assets = new AssetSet(nativeRoot); var cache = new SheetCache(assets); var workspace = new NativeWorkspace(assets, cache);
        var animated = ArtDocument.Create("A", 0, 64, 32, category: "outfit");
        animated.Edit(0, s => {
            s.Directions = 4;
            s.Frames = Enumerable.Range(0, 12).Select(i => {
                var bytes = new byte[8192]; bytes[0] = (byte)(i + 1); bytes[3] = 255;
                return new ArtFrame { Duration = 120 + i / 4, Layers = [new() { Rgba = bytes }] };
            }).ToList();
        });
        string file = Path.Combine(session, "art-" + animated.Id + ".json");
        byte[] packet = JsonSerializer.SerializeToUtf8Bytes(animated.CommitPacket(), ArtDocument.Json);
        File.WriteAllBytes(file, packet); string hash = Convert.ToHexString(SHA256.HashData(packet));
        Reject(() => workspace.CommitArtFile(file, "BAD", session, 0), "checksum");
        Reject(() => workspace.CommitArtFile(file, hash, nativeRoot, 0), "path escape");
        Reject(() => workspace.CommitArtFile(file, hash, session, 1), "stale native revision");
        Check(workspace.Revision == 0 && assets.Sheets[^1].Last == 3, "Invalid art left orphan sprites");
        var publish = JsonSerializer.SerializeToElement(workspace.CommitArtFile(file, hash, session, 0));
        Check(workspace.Revision == 1, "Art publish revision");
        workspace.CommitArtFile(file, hash, session, 0); Check(workspace.Revision == 1, "Retry duplicated assets");
        var groups = AssetSet.Groups(assets.Find("outfit", publish.GetProperty("selected").GetUInt32()));
        Check(groups.Length == 2 && groups[0].Type == 0 && groups[0].Sprites.Length == 4, "Animated outfit idle reference group");
        var g = groups[1];
        Check(g.PatternX == 4 && g.Frames == 3 && g.Sprites.Distinct().Count() == 12 && g.Phases[2].Min == 122, "Native directions/frames/timings");
        string saved = Path.Combine(root, "native-saved-" + Guid.NewGuid().ToString("N"));
        NativeSave.Export(assets, cache, saved); var reopened = new AssetSet(saved); var newCache = new SheetCache(reopened);
        Check(reopened.Find("item", 100).Source.Span.SequenceEqual(original), "Original unknown metadata changed");
        Check(AssetSet.Groups(reopened.Find("outfit", 1))[1].Sprites.Length == 12 && newCache.Sprite(g.Sprites[11]).Rgba[0] == 12, "Animation/pixels did not reopen");
        string legacy = Path.Combine(root, "legacy-source");
        if (!Directory.Exists(legacy))
        {
            var legacySource = new AssetSet(nativeRoot);
            new LegacyExport(legacySource, 1098, true).Export(legacy, new SheetCache(legacySource), true);
        }
        byte[] bulkInfo = NativeWorkspace.Scalar(1, 20).Concat(NativeWorkspace.Scalar(2, 20)).Concat(NativeWorkspace.Scalar(3, 1)).Concat(NativeWorkspace.Scalar(4, 16)).Concat(Enumerable.Range(0, 6400).SelectMany(_ => NativeWorkspace.Scalar(5, 1))).ToArray();
        assets.SetAppearance(new(102, "item", "", 1, NativeWorkspace.Scalar(1, 102).Concat(NativeWorkspace.Blob(2, NativeWorkspace.Blob(3, bulkInfo))).ToArray()));
        var bounded = JsonSerializer.SerializeToElement(workspace.StudioRead(JsonSerializer.SerializeToElement(new { action = "get_asset", category = "item", assetId = 102 })));
        Check(bounded.GetProperty("groups")[0].GetProperty("references").GetArrayLength() == 4096 && bounded.GetProperty("groups")[0].GetProperty("referencesTruncated").GetBoolean(), "Unbounded asset references in bridge response");
        Console.WriteLine("PASS: native art checksum/path/revision rejection, no invalid-input orphans, idempotence, 4 directions x 3 frames, distinct pixels/timings, Save/reopen and original metadata");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException) { return; } throw new Exception("Accepted invalid input: " + name); }
}
