import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'Klexir.Engine',
  description: 'Storage engine and database internals experiments for Klexir',
  base: '/Klexir.Engine/',
  cleanUrls: true,
  ignoreDeadLinks: true,

  head: [
    ['meta', { name: 'theme-color', content: '#7c3aed' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:title', content: 'Klexir.Engine' }],
    ['meta', { property: 'og:description', content: 'Storage engine and database internals experiments for Klexir' }],
  ],

  themeConfig: {
    siteTitle: 'Klexir.Engine',

    nav: [
      { text: 'Guide', link: '/guide' },
      {
        text: 'Klexir Ecosystem',
        items: [
          { text: 'Klexir.EventFlow', link: 'https://danny4897.github.io/Klexir.EventFlow/' },
          { text: 'Klexir.Actor', link: 'https://danny4897.github.io/Klexir.Actor/' },
          { text: 'Klexir.Workflow', link: 'https://danny4897.github.io/Klexir.Workflow/' },
          { text: 'Klexir.Engine', link: 'https://danny4897.github.io/Klexir.Engine/' },
          { text: 'Klexir.Runtime', link: 'https://danny4897.github.io/Klexir.Runtime/' },
          { text: 'Klexir.Lang', link: 'https://danny4897.github.io/Klexir.Lang/' },
        ],
      },
      { text: 'MonadicSharp', link: 'https://danny4897.github.io/MonadicSharp/' },
      { text: 'GitHub', link: 'https://github.com/Danny4897/Klexir.Engine' },
    ],

    sidebar: [
      { text: 'Guide', items: [{ text: 'Quick example', link: '/guide' }] },
    ],

    socialLinks: [
      { icon: 'github', link: 'https://github.com/Danny4897/Klexir.Engine' },
    ],

    footer: {
      message: 'Part of the <a href="https://danny4897.github.io/MonadicSharp/ecosystem">Klexir Ecosystem</a>, built on <a href="https://danny4897.github.io/MonadicSharp/">MonadicSharp</a>.',
      copyright: 'MIT — Danny4897',
    },
  },
})
