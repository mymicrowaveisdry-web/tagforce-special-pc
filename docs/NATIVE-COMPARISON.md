# One local native comparison

The attack-getter experiment now runs in Docker with PSPDEV GCC 15.2.0. Two trials
produce the same mismatch: 68 rebuilt bytes versus 84 original bytes, with 59
differing byte positions including the length difference. Actual byte validation
passes; no native match is claimed. Original compiler/version/settings remain unknown.

## Confirmed input and mapping

| Field | Value |
| --- | --- |
| Module | `EBOOT.BIN` |
| Module SHA-256 | `8087681f81f87a87471591752b3c0c9bc3cec48ffff15a115a1e2525f670c975` |
| Runtime address / load base | `0x0881F334` / `0x08804000` |
| ELF virtual address / file offset | `0x0001B334` / `0x0001B3F4` |
| Candidate length | 84 bytes |
| Original function SHA-256 | `3f0a901b2457890da45b057c87573d65ac9595ae2bd9560b2de269f74099e655` |
| Source section | Index 1, executable/allocated PROGBITS; name strings erased |
| Source segment | PT_LOAD 0: file offset `0xC0`, ELF virtual address 0 |
| External property-record helper | Runtime `0x0881ED64`; ELF virtual address `0x0001AD64` |

One PSP `R_MIPS_26` relocation affects the call at function offset +8. The trial
links at the original ELF virtual address with the helper at its original ELF
address, then compares **unrelocated bytes without masking**. Comparing raw file
bytes directly with a runtime-linked image would be incorrect. Static boundaries
agree with neighboring candidates and both return paths; this is not a byte match.

## Run locally

On Windows with Docker Desktop, use the pinned SDK image and wrapper:

```powershell
.\native\Invoke-PspComparison.ps1 -Build -Command test
.\native\Invoke-PspComparison.ps1 -Command run -ModulePath MODULE_PATH
.\native\Invoke-PspComparison.ps1 -Command validate -ModulePath MODULE_PATH
```

The Dockerfile pins the official SDK image digest. The wrapper mounts the native
sources and original module read-only, writes only under ignored `analysis/`, and
disables container networking during compilation. It uses `psp-gcc`, the compiler
name supplied by this SDK. Docker Desktop's existing WSL backend is sufficient;
a separate Ubuntu distribution is not required. See the [official Docker guide](https://pspdev.github.io/installation/docker.html).

From the repository root, supply your own matching decrypted ELF. `MODULE_PATH`
and `COMPILER_PATH` below stand for local paths; never commit those private values.

```text
python -m unittest discover -s native -p "test_*.py"
python native/compile_compare.py inspect --module MODULE_PATH
python native/compile_compare.py run --module MODULE_PATH --compiler COMPILER_PATH
python native/compile_compare.py validate --module MODULE_PATH
python native/verify_getter_fixture.py
dotnet run --project TagForceSpecial.Tools --no-build -- research-check --local
```

`native/target.json`, `card_attack.c` and `card_attack.ld` pin the target, proposed
behavior, first-trial flags and link addresses. The script records compiler
version, commands, source/config hashes, original/rebuilt lengths, actual hex
bytes and per-byte differences in ignored `analysis/native/card-attack/`.
No proprietary bytes appear in public summaries. Exit codes: 0 successful
inspection/validation or equal trial; 2 unequal trial; 3 compiler blocker;
1 invalid input/evidence. Validation may pass for an honestly reported mismatch.

The validator independently extracts original and rebuilt byte streams. It
rejects changed inputs, mismatched reports, unlinked objects and unresolved
relocations; trials remove known stale build outputs before compiling. The test
suite uses synthetic ELF data and cannot establish an original native match.

## Agent work units and acceptance

| Unit | Responsibility | Independent gate |
| --- | --- | --- |
| A — analysis | Module, MIPS, section/relocation mapping, boundaries and classification | Re-extract/hash the original function; review B's mapping and outputs |
| B — native | Reconstruct C and run compiler trials | A/reviewer reruns compilation and actual byte validation |
| C — behavior | C# reconstruction and PPSSPP differential fixtures | Verify observed outputs against capture provenance and run .NET tests |

The queue includes the attack getter and two lookup dependencies. Lookup bounds
and invalid-ID behavior remain separate research; the managed helper reconstructs
only packed-word extraction. Four historical getter samples agree, but their
capture lacks a retained named anchor/raw debugger transcript. Sentinel/boundary
tests are synthetic and explicitly labeled.

`verify_getter_fixture.py` rehashes the private capture, independent packed-input
listing and original capture script, then joins each observed output to its input.
It checks historical projection integrity; it does not replay the original capture.

Next: inspect the recorded MIPS diff and make bounded compiler/source trials.
A mismatch is a valid research result. Only a separately rebuilt,
reviewed zero-difference result can be proposed for the native-match register.

## Change review and executed checks

Added one native compiler experiment, strict queue/coverage validation, independent
match-review metadata gates, getter differential tests and PR progress checks.
The existing solution, candidate inventories and zero-byte native baseline remain.

| Executed command | Result |
| --- | --- |
| `dotnet restore ygo-tag-force-special-decomp.slnx --locked-mode` | Passed |
| `dotnet build ygo-tag-force-special-decomp.slnx --no-restore` | Passed; no warnings/errors |
| `dotnet test ygo-tag-force-special-decomp.slnx --no-build --no-restore` | 145 passed |
| `python -m unittest discover -s native -p "test_*.py"` | 24 passed; synthetic infrastructure tests |
| Docker native `run` / `validate` against the matching local module | Two identical mismatches: original 84 bytes, rebuilt 68 bytes; validation passed |
| `python native/verify_getter_fixture.py` | Four historical inputs/outputs and three provenance files verified |
| `research-check --local`, `progress --check`, `progress --verify-test-counts` | Passed |

The PR regenerate/diff sequence was also exercised in an isolated local Git index:
clean generation passed and deliberately stale progress was rejected. The actual
workspace index was not changed.

Limits: no fresh PPSSPP capture or native match; the original compiler is unknown.
Hosted CI cannot reproduce private input checks. P3 deeper differential captures
remain queued before effects/chains and a playable PC duel.
