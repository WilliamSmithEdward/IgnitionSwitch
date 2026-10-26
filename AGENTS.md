I have a Novation launchkey MK3 37

I want to make a C# WinUI 3 app to create music with it

it should be as easy as possible for me to use

it should be deeply compatible with all buttons/features of the hardware

the UI should look slick and modern

it should use prettyCSharp, and should address all errors/warnings/information alerts from the .net analyzer (including things like collection impression can be simplified etc.)

it should feel like the software was made for my launchkey (wider vision is it should work with any launchkey product... but support for my specific device comes first)

F:\GitHub\IgnitionSwitch\hardware_images

create a public repo on github

## Development conventions

- IgnitionSwitch is a fully standalone product: sound generation, recording, playback and project storage must not require another DAW.
- Keep device integration behind hardware-neutral contracts so other hardware types can be added. Prioritize the Launchkey MK3 37 implementation and usability for people with limited music-production experience.
- Follow DRY principles. Organize source by responsibility in namesake files and folders, and maintain the `src`, `tests` and documentation groups in `IgnitionSwitch.slnx` for Visual Studio Solution Explorer.
- Keep the workspace focused on music creation: remove persistent tutorial copy, marketing slogans and the on-screen piano keyboard. Put occasional guidance and detailed device information behind compact help/device controls. Premium usability comes from clear hierarchy, precise alignment and predictable interactions; use layout grids for aligned text rather than spaces.
- Maintain `docs/devices/novation_launchkey_mk3_37.md` as a living device reference. Update protocol facts, implemented mappings, verification evidence and remaining gaps whenever device behavior changes. Distinguish official specifications from observed hardware behavior.
- With every implementation commit, review and update `docs/in_memory_engine/music_api_coverage.md` with supported behavior, evidence, member counts and remaining gaps. Keep this report separate from reports for other apps.
