# SharpAgent web UI prototype

Open [index.html](index.html) in a modern browser.

This standalone HTML is a visual and interaction prototype for the future React web application. It uses conversation-first coding-agent patterns familiar from desktop tools, but it is deliberately a responsive browser interface, not a desktop-app clone.

The functional specification excludes authentication from the trusted-local MVP, so the prototype opens directly in the active agent workspace.

## Suggested review path

1. Start in **Agent workspace** and inspect the task thread, plan/todos, workspace evidence, approval request, composer, and details panel.
2. Select **Approve once** for the patch, then separately approve the focused test. The workspace, timeline, terminal, review, dashboard, and statistics adapt to the completed state.
3. Open **Run controls** to cancel or archive a session, resume it with an optional follow-up instruction, or review representative context-compaction, configured-limit, provider-error, and stream-interruption recovery states.
4. Start a **New session** and choose **Plan only** or **Controlled execute**. The OpenRouter profile remains plan-only until its bounded validation is completed in Administration.
5. Open **Administration** from the header or local profile. Adjust session limits in **Policy & limits**, then validate the OpenRouter profile under **Providers** or **Model profiles**. Its focused category rail is inspired by the supplied OpenCode reference while remaining web-appropriate.
6. Visit **Dashboard**, **Archive**, and **Statistics** as supporting web application views. The browser hash represents the corresponding future React Router path, including session review and change-review paths.
7. Change between **Studio**, **Midnight**, **Ocean**, and **Forest** in Administration → Appearance. Theme, demo session state, limits, compaction count, and profile-validation state persist in browser storage.

No network, model, filesystem, shell, credentials, or database action occurs in the prototype. The recovery, validation, approval, and persistence states are visual demonstrations of the intended future application behavior.
