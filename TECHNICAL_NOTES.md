# Technical Notes

## DS_B.0.4.19 validated sites

`0xA48D5B, 0xA48D8D, 0xA48DBF, 0xA48DE8, 0xA48E11, 0xA48E3A, 0xA48E63`

## DS_B.0.4.23 verified method

The current interop assembly identifies `DetermineEstablishTalents` as MethodDef token `0x0600C00E`.
`MethodAddressToToken.db` maps that method to native RVA `0xA61980`.

Disassembly of that exact native function gives these seven RNG CALL sites:

`0xA61ABB, 0xA61AED, 0xA61B1F, 0xA61B48, 0xA61B71, 0xA61B9A, 0xA61BC3`

Expected five-byte CALL signatures:

- `E8 10 A2 ED 02`
- `E8 DE A1 ED 02`
- `E8 AC A1 ED 02`
- `E8 83 A1 ED 02`
- `E8 5A A1 ED 02`
- `E8 31 A1 ED 02`
- `E8 08 A1 ED 02`

All seven calls resolve to native target RVA `0x393BCD0` in the supplied DS_B.0.4.23 `GameAssembly.dll`.

## v2.1.5 failure

v2.1.5 used `0xA60CBB ... 0xA60DC3`. Those addresses were exactly `0xE00` before the real CALL sites. The existing safety verification detected the mismatch before writing executable memory, so the incorrect build failed closed rather than patching unrelated code.
