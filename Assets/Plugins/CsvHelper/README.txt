CsvHelper 34.0.0
Source: https://www.nuget.org/packages/CsvHelper/34.0.0
Upstream: https://github.com/JoshClose/CsvHelper
Binary: lib/netstandard2.1/CsvHelper.dll (unmodified)
License: MS-PL OR Apache-2.0; see LICENSE.txt.

Dependency: System.Runtime.CompilerServices.Unsafe 6.1.2
Source: https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe/6.1.2
Binary: lib/netstandard2.0/System.Runtime.CompilerServices.Unsafe.dll (unmodified)
License: MIT; see Unsafe-LICENSE.txt.

Installed directly as managed Unity plugins; no NuGet package manager is required.
Both DLLs must remain enabled for Editor and player platforms.
The accompanying XML files provide API documentation in the IDE.

Version 34 changes the public API from version 33. Use the matching documentation:
https://joshclose.github.io/CsvHelper/

Validation (2026-11-09): Unity 6000.3.18f1 imported and compiled these plugins in
an isolated minimal project. Four CSV checks passed: quoted UTF-8/multiline
reading, header-only input, write/read round trip, and invalid integer rejection.
Full-project integration and player/IL2CPP builds have not been validated.
