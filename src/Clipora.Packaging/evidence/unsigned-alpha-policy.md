# Unsigned alpha release policy

Clipora 0.1.0 build 1 is distributed as an unsigned alpha build.

- Every Setup, Portable and source archive is accompanied by SHA-256 checksums.
- GitHub Releases is the only intended binary distribution channel.
- The Inno Setup installer does not request hidden elevation or install services.
- Windows may show an unknown-publisher warning until an Authenticode certificate is introduced.
- Release automation remains ready for Authenticode signing when a certificate becomes available.

This is an explicit release decision, not a missing build step.
