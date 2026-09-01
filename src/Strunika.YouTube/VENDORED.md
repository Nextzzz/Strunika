# YoutubeExplode, vendored

Source: https://github.com/Tyrrrz/YoutubeExplode, tag **6.6.2**, folder
`YoutubeExplode/` copied whole (minus its `.csproj`); licence in
`LICENSE-YoutubeExplode.txt` (MIT).

## Why not the NuGet package

The package is produced by merging its dependencies (AngleSharp, PowerKit,
JsonExtensions, Deorcify) into one assembly with Binternal. From 6.6.1 on that
merge leaves duplicate type definitions in the file — 194 names defined twice,
among them compiler-generated ones such as
`<PrivateImplementationDetails>/__StaticArrayInitTypeSize=82`. ECMA-335 forbids
that, the runtime tolerates it (it resolves by token), and every tool that goes
by name breaks: the iOS trimmer's "Collect Unmarked Members" step (MT2231),
UWP's .NET Native (upstream #967). 6.6.0 and 6.5.x are clean, but only 6.6.2
knows the current YouTube (PR #965, August 2026).

Compiling the same source here, with the dependencies as ordinary package
references, yields a clean assembly. The iOS head references this project; the
Windows head and the desktop app keep the NuGet package, where nothing trims.

## Updating

1. Download the new tag's archive; replace every folder and the two root `.cs`
   files, keep `FodyWeavers.xml` in step with upstream.
2. Copy the versions from upstream `Directory.Packages.props` into the csproj.
3. Build; the mobile iOS head must publish with trimming on.
4. Note the tag here.
