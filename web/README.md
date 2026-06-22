# YT Rec — landing page

Static, self-contained landing page for [ytrec.resonaframe.com](https://ytrec.resonaframe.com).
No build step, no framework. Just `index.html` + `favicon.svg`.

## Files
- `index.html` — the whole page (inline CSS/JS, bilingual zh-Hant/English, inline brand SVG, Ko-fi embed)
- `favicon.svg` — app-icon mark

## Preview locally
```bash
python3 -m http.server 8011 --directory web
# open http://localhost:8011
```

## Deploy — Cloudflare Pages (recommended)
1. Cloudflare dashboard → **Workers & Pages → Create → Pages → Connect to Git** → pick this repo.
2. Build settings: **Framework preset: None**, **Build command: (empty)**, **Build output directory: `web`**.
3. Deploy. You get a `*.pages.dev` URL.
4. **Custom domain**: Pages project → Custom domains → add `ytrec.resonaframe.com`. If `resonaframe.com` is already on Cloudflare, it adds the CNAME automatically; otherwise add a CNAME for `ytrec` → the `pages.dev` target.

## Before it's fully live
- [ ] Make the GitHub repo public.
- [ ] Create a GitHub **Release** (e.g. `v1.0`) and upload the macOS `.dmg` as an asset. The "下載 .dmg" button points to `releases/latest`. (Optional: name the asset `YT-Rec.dmg` and link `releases/latest/download/YT-Rec.dmg` for a one-click direct download.)
- [ ] Ko-fi is wired to the `ytrec` account (inline panel + floating button). Reset the Ko-fi API/webhook token if it was shared anywhere — the widgets don't need it.
- [ ] (Polish) Add `og.png` (1200×630) for social link previews; `<meta og:image>` already points to it.

## Notes
- Brand colors and the logo mark come from `../shared/branding/CIS-YTRec.md`. Keep the red family and the viewfinder + rounded-triangle mark; don't make it look like YouTube.
- Inter is loaded from Google Fonts for Latin/numbers; Chinese falls back to system PingFang TC / Noto Sans TC / Microsoft JhengHei.
