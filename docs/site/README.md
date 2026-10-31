# GitHub Pages landing page

Static HTML, CSS, and JavaScript with no build dependencies. Google Fonts is optional; system fonts are used if it is unavailable.

The page lives in `docs/site/`. In repository Settings > Pages, choose **GitHub Actions** as the source. The included `.github/workflows/pages.yml` publishes this folder on default-branch pushes that change the page or workflow, or on manual dispatch. App builds and workers are not run by this workflow.

The page uses the actual application artwork and a screenshot with example account data. It describes first-launch preferences and links to source/build instructions rather than promising a precompiled release.

For a local preview, serve this directory with any static HTTP server. Alternatively, open `index.html` directly; if clipboard access is unavailable, the copy button selects the commands for manual copying.
