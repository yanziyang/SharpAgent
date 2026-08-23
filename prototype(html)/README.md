# SharpAgent web UI prototype

Open [index.html](index.html) in a modern browser.

This standalone HTML is a visual and interaction prototype for the future React web application. It uses conversation-first coding-agent patterns familiar from desktop tools, but it is deliberately a responsive browser interface, not a desktop-app clone.

The functional specification excludes authentication from the trusted-local MVP, so the prototype opens directly in the active agent workspace.

## Suggested review path

1. Start in **Agent workspace** and inspect the task thread, plan/todos, workspace evidence, approval request, composer, and details panel.
2. Select **Approve once** for the patch, then separately approve the focused test. The workspace, timeline, terminal, review, dashboard, and statistics adapt to the completed state.
3. Start a **New session** and choose **Plan only** or **Controlled execute**.
4. Visit **Dashboard**, **Archive**, and **Statistics** as supporting web application views.
5. Open **Administration** from the header or local profile. Its focused category rail is inspired by the supplied OpenCode reference while remaining web-appropriate.
6. Change between **Studio**, **Midnight**, **Ocean**, and **Forest** in Administration → Appearance. The selection persists in browser storage.

No network, model, filesystem, shell, credentials, or database action occurs in the prototype.
