---
layout: home

hero:
  name: "Klexir.Engine"
  text: "Storage engine internals"
  tagline: Built from the file up — pages, a buffer pool, a B-Tree, a write-ahead log, 2PL transactions, and the relational operators a query planner would sit on top of.
  actions:
    - theme: brand
      text: Quick example
      link: /guide
    - theme: alt
      text: Full README on GitHub
      link: https://github.com/Danny4897/Klexir.Engine
    - theme: alt
      text: Klexir Ecosystem
      link: https://danny4897.github.io/MonadicSharp/ecosystem

features:
  - title: Durable by construction
    details: Writes go through a write-ahead log before they're acknowledged — WalRecovery replays what a crash never let the store see.
  - title: A real page-backed index
    details: PagedBTree's nodes live in pages, not in memory — genuinely durable across a restart, not just an in-memory B-Tree with a save button.
  - title: Part of the Klexir Ecosystem
    details: One of 7 experimental .NET repos exploring systems-programming concepts — see the full ecosystem on MonadicSharp's docs.
---
