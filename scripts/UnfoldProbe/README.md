# Heavy Markdown unfold probe

Run `dotnet run --project scripts/UnfoldProbe/UnfoldProbe.csproj -c Release -- --benchmark` from the repository root.

This opt-in harness uses the actual StickyNoteWindow and WPF dispatcher, with a dedicated data store at `artifacts/unfold-probe`. It does not load the user's notes. Launch the built `UnfoldProbe.exe` without arguments to display two sample windows (spine enabled/disabled).

The fixture contains 39,699 characters, 1,531 lines, 80 sections/tables and eight distinct generated 1920×1080 PNGs. It is below MarkdownRenderer's plain-text fallback limits. The rendered document contains 971 top-level blocks.

Each of the eight combinations runs 12 unfold samples: spine on/off, animation on/off, cached document/forced rebuild. Expanded size is 680×680 DIPs; folded width is 360 DIPs. Animation duration is 150 ms. Timing starts before ToggleFold and ends after the animation callback, UpdateLayout and dispatcher ApplicationIdle. It measures application/dispatcher completion, not pixels reaching the monitor. Samples run sequentially, so small differences between groups are not causal evidence. Startup timing includes a settling delay and is not a pure render measurement.

`forceRebuild` invalidates only the retained-document flag before unfolding. This is a diagnostic comparison, not an exact execution of the previous implementation. Decoded images remain cached in both cases. `reused` checks that the first document block retained its identity; production rebuilds clear the entire block collection.

Results and the reusable note/PNG files are written to `artifacts/unfold-probe`. The harness exits after benchmark mode. The application implementation is not modified by the probe.

## Measurement on 2026-09-11 (Release, this machine)

Median unfold duration, milliseconds (12 samples each):

| Spine | Animation | Retained document | Forced rebuild |
|---|---|---:|---:|
| Off | Off | 403.3 | 877.8 |
| On | Off | 345.9 | 859.4 |
| Off | 150 ms | 662.9 | 1113.0 |
| On | 150 ms | 654.2 | 1113.7 |

All retained-document groups reused the document in 12/12 samples. Forced-rebuild groups did so in 0/12 samples. The optimization removes a substantial part of the cost, but this heavy fixture still takes approximately 0.65 seconds to unfold with animation. A spine-specific slowdown was not reproduced. Remaining layout/font/visibility invalidations require separate profiling; no root-cause-complete claim is supported by this measurement.

Native visual inspection was attempted after measurement, but Computer Use's application approval timed out and the interactive windows were not launched. The timings above come from actual WPF windows exercised by the harness, not manual UI observation.

## Follow-up on 2026-09-12

The `--stable-font` diagnostic binds the retained body directly to FontSize rather than the fold-dependent ContentFontSize. It writes `results-stable-font.json`. Without animation this reduced the representative duration to 16–17 ms, but animation still incurred repeated layout.

Production changes now keep the body's font size stable and keep the body collapsed until the height animation finishes. The dedicated preview handles the folded text size. A fresh run without diagnostic overrides produced:

| Spine | Animation | Retained document (ms) | Forced rebuild (ms) |
|---|---|---:|---:|
| Off | Off | 16.2 | 716.3 |
| On | Off | 14.9 | 670.9 |
| Off | 150 ms | 133.6 | 773.6 |
| On | 150 ms | 133.6 | See results.json |

Every retained-document group reused the body in 12/12 trials. Measurements use WPF animation-clock/dispatcher completion, not display presentation latency; configured animation duration can differ from the wall time measured at the method call. The earlier run is preserved in `results-before-final-fix.json`. These runs occurred at different times and are not controlled hardware benchmarks.

All 616 automated tests passed. Native inspection then succeeded after enabling taskbar visibility in the interactive harness: both sample windows visibly contain the image, formatted text and table, and both were folded and unfolded through actual mouse input. Both comparison windows were left open for inspection. Initial image decoding and first-time Markdown construction remain outside the warm-unfold optimization.
