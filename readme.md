# C# implementation of LZMA and 7zip

This project provides a translation of the LZMA SDK, including both the LZMA and LZMA2 algorithms as well as the 7zip container format in fully managed C#

The current state is working but the semi-automatic translation is still showing in a lot of places. Long term goals are clean up and optimization of the codebase and better integration into the managed world.

The current API and implementation is not stable and subject to change during the cleanup process.

### Roadmap:
- Update to the latest version of the reference implementation
- Design a proper API for 7z archives, the current API is a placeholder
- Implement the projection onto unsafe pointers for performance

[![Discussion of Implementation and Features at https://gitter.im/weltkante/managed-lzma](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/weltkante/managed-lzma?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge) for discussions that don't warrant a github issue.
