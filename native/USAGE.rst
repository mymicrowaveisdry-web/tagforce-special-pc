Local PSP MIPS compiler experiment
==================================

This experiment reconstructs one 84-byte card attack getter. It does not update
native-match records. C# tests, pseudocode and matching report hashes do not
establish a native match.

Inputs and mapping
------------------

``target.json`` pins the whole original module and function SHA-256 values.
Supply your own lawful, unmodified decrypted ELF EBOOT.BIN locally. No binary is
distributed. The runtime address 0x0881F334 maps through load base 0x08804000
to ELF virtual address 0x1B334, file offset 0x1B3F4. The original executable
section has index 1; its name is erased. The function has one R_MIPS_26 PSP
relocation at +8. The experiment compares unrelocated bytes, linking the trial
at ELF address 0x1B334 and helper property_record at ELF address 0x1AD64.
There is no relocation masking. Rebuilt function length comes from its symbol
table, so longer or shorter output cannot be mistaken for a match.

Commands (from repository root)
------------------------------

Use Python 3.10+ (the existing .venv Python works)::

    python -m unittest discover -s native -p "test_*.py"
    python native/compile_compare.py inspect --module PATH_TO_LOCAL_EBOOT
    python native/compile_compare.py run --module PATH_TO_LOCAL_EBOOT --compiler PATH_TO_MIPSEL_PSP_GCC
    python native/compile_compare.py validate --module PATH_TO_LOCAL_EBOOT

All outputs go to ignored ``analysis/native/card-attack/`` by default. Alternate
``--out`` directories must also be beneath analysis/. Inspection records section,
segment mapping and relocation entries. Run records compiler version, exact
commands/flags, C/linker/target hashes, original and rebuilt lengths, hex bytes,
SHA-256 values and per-byte diffs. Its exit status is 0 for equal bytes, 2 for a
mismatch, 3 for an absent/failing compiler, and 1 for invalid evidence/input.
Inspect and validate return 0 when their own checks succeed. Validation recomputes
both byte streams directly from the original ELF and rebuilt ELF, and verifies
source/config identities. It accepts a correctly reported mismatch; it does not
certify native match eligibility. A separate reviewer must rerun compilation and
validation and review boundaries/provenance before any match record changes.

Docker toolchain and next steps
------------------------------

Persistent Docker Desktop container
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

The root compose.yaml starts the ``tagforce-research`` project with the named
``tagforce-psp-python`` container. It stays running and remains listed when stopped.
The restart policy is unless-stopped. From the repository root::

    docker compose up --build -d
    docker compose exec psp-python sh
    docker compose exec -T psp-python python3 -m unittest discover -s native -p "test_*.py"
    docker compose stop
    docker compose start

The image includes PSP GCC, Python and pinned pycdlib, pypdf, websocket-client and
Pillow packages. Source, configuration, tools, tests and data mounts are read-only;
``/source/analysis`` is writable and persists in the host analysis directory.
Host edits appear through the mounts. No game inputs are baked into the image.
The service provides offline Python processing and compilation. PPSSPP debugger
and Windows window-capture operations run on the Windows host.

For example, run the native validator from the Desktop terminal or Compose exec,
using a module path under ``/source/data`` and an output path under
``/source/analysis``. The health check verifies the compiler and Python imports.

Temporary comparison containers
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

Docker Desktop's Linux engine runs the official SDK without a separate Ubuntu
installation. The checked-in Dockerfile pins the SDK image digest and adds Python.
The SDK provides ``psp-gcc``; older toolchains can still be selected with --compiler.
From PowerShell in the repository root::

    .\native\Invoke-PspComparison.ps1 -Build -Command test
    .\native\Invoke-PspComparison.ps1 -Command run -ModulePath PATH_TO_LOCAL_EBOOT
    .\native\Invoke-PspComparison.ps1 -Command validate -ModulePath PATH_TO_LOCAL_EBOOT

Runtime containers have networking disabled, a read-only root and read-only native
source/module mounts. Writable evidence goes to ignored
``analysis/native/card-attack-docker/``. Use ``-OutputDirectory`` to repeat a trial
in a fresh ignored directory. Building the image requires internet access.

GCC 15.2.0 produced 68 bytes versus the original 84 bytes; two trials repeat the
same mismatch and actual byte validation passes. See experiment-status.json for
toolchain identity and sanitized comparison hashes. Native matched bytes remain 0.
Original compiler/settings are still unknown. Inspect the diff before bounded
source/flag experiments; a byte match is not required for the first C# PC release.

Remaining uncertainty: inferred function bounds, external helper contract, invalid
card IDs and original optimization/compiler version need independent research.
No functions or executable bytes become native matched from this scaffold.
