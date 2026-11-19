# CLAUDE.md

## Team knowledge (saltsys plugins)

This repo uses the Saltsys team skills from the `saltsys` plugin marketplace
(github.com/TobiasHabluetzel/saltsys-claude): backend-apis, frontend,
insurance-domain, team-workflow. Check the relevant skill before working on
NIS GraphQL, Automated Claims, the claim flow, branding, config or deployment.

`.claude/settings.json` registers the marketplace. If the skills don't show up
(type `/backend` in Claude Code), install them once for this repo:

    /plugin install backend-apis@saltsys
    /plugin install frontend@saltsys
    /plugin install insurance-domain@saltsys
    /plugin install team-workflow@saltsys

When you learn something reusable about our stack (an API quirk, a working
query, a deploy fix, a convention), do not keep it in local memory. Draft it
as a skill (or an addition to an existing skill) and propose a PR against
saltsys-claude, following the `team-workflow:contributing-skills` skill.
If a skill turns out to be wrong or outdated, propose the fix the same way.
