# C# implementation of LZMA and 7zip

This project provides a translation of the LZMA SDK, including both the LZMA and LZMA2 algorithms as well as the 7zip container format in fully managed C#

In the current state the project hides the translated code behind a prototype public API, cleanup of the actual implementation will follow at a later stage.

A [nuget package](https://www.nuget.org/packages/ManagedLzma) for the desktop framework exists and is currently in finalization phase. A portable library is theoretically possible but currently does not build properly for all targets, for details see issue #8

### Roadmap:
- Complete missing functionality for the nuget package
- Writing documentation for the nuget package
- Implement an Universal Windows library (Windows 10) to achieve native C++ performance
- Implement the projection onto unsafe pointers for performance

[![Discussion of Implementation and Features at https://gitter.im/weltkante/managed-lzma](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/weltkante/managed-lzma?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge) for discussions that don't warrant a github issue.
