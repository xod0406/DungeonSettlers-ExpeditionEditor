# DS_B.0.4.18 compatibility verification

Target: Dungeon Settlers v0.4.18 (Windows x64 / BepInEx 6 IL2CPP)

The v0.4.17 build stopped safely at the first old talent patch site because the bytes no longer matched.
For v0.4.18, direct disassembly of the supplied `GameAssembly.dll` identified the same seven-call talent RNG pattern at:

- target sum: `0xA48D5B` -> `E8 C0 BC EB 02`
- Strength: `0xA48D8D` -> `E8 8E BC EB 02`
- Constitution: `0xA48DBF` -> `E8 5C BC EB 02`
- WillPower: `0xA48DE8` -> `E8 33 BC EB 02`
- Intelligence: `0xA48E11` -> `E8 0A BC EB 02`
- Agility: `0xA48E3A` -> `E8 E1 BB EB 02`
- Perception: `0xA48E63` -> `E8 B8 BB EB 02`

All seven calls resolve to the same RNG routine at RVA `0x3904A20`, matching the v0.4.17 code structure.

The existing v2.1.2 gameplay/UI/persistence logic is retained; only compatibility constants and version metadata were updated in the install DLL.
