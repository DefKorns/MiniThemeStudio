using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniThemeStudio.Core.Fonts;
using MiniThemeStudio.Infrastructure.Fonts;
using Xunit;
using Xunit.Abstractions;

namespace MiniThemeStudio.Infrastructure.Tests.Fonts;

/// <summary>
/// Diagnostic: quais caracteres da font de referência CFW (title.fnt hakchi)
/// NÃO existem na Super Metroid font rasterizada — agrupados por categoria.
/// Run: dotnet test --filter "CompareHakchiTitleFntWithRasterizer" --logger "console;verbosity=detailed"
/// </summary>
public sealed class FontComparisonDiagnosticsTests(ITestOutputHelper output)
{
    private const string HakchiFntPath = @"D:\games\SNESCE\hakchi-CE\user_mods\title.fnt";
    private const string OtfZipPath    = @"E:\Downloads\super-metroid-intro-snes.otf.zip";
    private const string TtfZipPath    = @"E:\Downloads\super-metroid-intro-snes.ttf.zip";

    [Fact]
    public void CompareHakchiTitleFntWithRasterizer()
    {
        if (!File.Exists(HakchiFntPath))
        {
            output.WriteLine($"[SKIP] {HakchiFntPath} nao encontrado.");
            return;
        }
        var fontPath = File.Exists(OtfZipPath) ? OtfZipPath
                     : File.Exists(TtfZipPath) ? TtfZipPath
                     : null;
        if (fontPath is null)
        {
            output.WriteLine("[SKIP] Ficheiro .zip da Super Metroid nao encontrado.");
            return;
        }

        var reference  = MiniFontReader.Read(HakchiFntPath);
        var rasterizer = new TrueTypeFontRasterizer();
        var rasterized = rasterizer.Rasterize(fontPath, new TrueTypeRasterOptions(
            FontSize:   (int)Math.Abs(reference.Info.FontSize),
            AntiAlias:  reference.Info.Smooth,
            AtlasWidth: (int)reference.Common.ScaleW));

        var refIds  = reference.Glyphs.Select(g => g.Id).ToHashSet();
        var rastIds = rasterized.Glyphs.Select(g => g.Id).ToHashSet();

        // Caracteres que o CFW espera mas a Super Metroid nao tem
        var missing = refIds.Except(rastIds).OrderBy(x => x).ToList();

        output.WriteLine($"Font de referencia : {reference.Info.FontName} — {reference.Glyphs.Count} glyphs");
        output.WriteLine($"Font rasterizada   : {rasterized.Info.FontName} — {rasterized.Glyphs.Count} glyphs");
        output.WriteLine($"Em falta na rast.  : {missing.Count} caracteres\n");

        // Agrupar por categoria Unicode
        var groups = new (string Label, Func<uint, bool> Match)[]
        {
            ("ASCII imprimivel basico (32-126)",           id => id is >= 32 and <= 126),
            ("Latin-1 maiusculas acentuadas (192-214)",    id => id is >= 192 and <= 214),
            ("Latin-1 restantes acentuados (216-255)",     id => id is >= 216 and <= 255),
            ("Latin Extended-A (256-383)",                 id => id is >= 256 and <= 383),
            ("Simbolos e pontuacao geral (8192-8303)",     id => id is >= 8192 and <= 8303),
            ("Outros Unicode",                             _ => true),
        };

        var assigned = new HashSet<uint>();
        foreach (var (label, match) in groups)
        {
            var inGroup = missing
                .Where(id => !assigned.Contains(id) && match(id))
                .ToList();

            if (inGroup.Count == 0) continue;

            if (label != "Outros Unicode")
                foreach (var id in inGroup) assigned.Add(id);

            output.WriteLine($"-- {label} ({inGroup.Count} em falta) --");
            for (var i = 0; i < inGroup.Count; i += 8)
            {
                var row = inGroup.Skip(i).Take(8)
                    .Select(id => char.IsControl((char)id) || id > 0xFFFF
                        ? $"U+{id:X4}      "
                        : $"U+{id:X4}({(char)id})  ");
                output.WriteLine("  " + string.Concat(row));
            }
            output.WriteLine("");
        }

        // Sumario ASCII 32-126
        var asciiMissing = missing.Where(id => id is >= 32 and <= 126).Select(id => (char)id).ToList();
        var asciiInRef   = refIds.Count(id => id is >= 32 and <= 126);
        output.WriteLine($"-- Sumario ASCII 32-126 --");
        output.WriteLine($"  Presentes no rasterizado : {asciiInRef - asciiMissing.Count} / {asciiInRef}");
        output.WriteLine(asciiMissing.Count > 0
            ? $"  Em falta : {string.Join(" ", asciiMissing)}"
            : "  Em falta : (nenhum)");

        Assert.NotNull(reference);
        Assert.NotNull(rasterized);
    }
}
