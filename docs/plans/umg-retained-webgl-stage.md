# UMG retained scene and WebGL stage

Goal: remove animation-driven scene reconstruction and move appearance composition to a shared WebGL2 context without changing editing semantics.

Implementation is performed directly by the main agent (project convention), not delegated.

## Stage 1 — retained scene
- Separate content invalidation from frame updates in UMGStage.vue; preserve object identity during transform/opacity/material animation.
- Replace per-object absolute Fabric clip layers with direct polygon clipping in the canvas render context.
- Drive timeline playback with requestAnimationFrame instead of setInterval.
- Verify actual BP_Title animation, scrub and empty selection, clipping and asset switching; build and commit explicit paths.

## Stage 2 — shared GPU composition
- Introduce an appearance renderer with one WebGL2 context and retained textures/programs.
- Adapt MaterialWebGLRenderer to external context and render targets, keeping standalone material preview compatible.
- Compose images/material outputs in source draw order, preserve alpha/additive semantics and polygon clips.
- Keep Fabric editing overlays/hit testing separate; retain explicit fallback where unsupported.
- Verify visible scene, animation updates, resource lifetimes and no per-frame context creation; build and commit explicit paths.

## Boundaries
Do not include unrelated particle/backend changes in commits. Do not change FACE layout defaults during this refactor. Do not claim performance improvements without measured runtime evidence. No full integration regression suite.
