import { chromium } from 'playwright-core';
import { mkdir } from 'node:fs/promises';

const baseUrl = process.env.GITCANDY_BASE_URL ?? 'http://127.0.0.1:5080';
const executablePath = process.env.CHROME_PATH;
if (!executablePath) throw new Error('CHROME_PATH is required.');
await mkdir('output/playwright', { recursive: true });
const browser = await chromium.launch({ executablePath, headless: true });
try {
  for (const viewport of [{ name: 'desktop', width: 1440, height: 900 }, { name: 'mobile', width: 390, height: 844 }]) {
    const page = await browser.newPage({ viewport });
    await page.goto(`${baseUrl}/Account/Login`, { waitUntil: 'networkidle' });
    if ((await page.locator('h1').textContent())?.trim() !== 'Sign in') throw new Error('Login page did not render.');
    await page.screenshot({ path: `output/playwright/login-${viewport.name}.png`, fullPage: true });
    await page.getByRole('link', { name: 'Forgot password?' }).click();
    await page.getByLabel('Email').fill('nonexistent@example.com');
    await page.getByRole('button', { name: 'Send reset link' }).click();
    await page.getByText('If an account matches that address').waitFor();
    await page.screenshot({ path: `output/playwright/recovery-${viewport.name}.png`, fullPage: true });
    await page.close();

    const helpPage = await browser.newPage({ viewport });
    const browserErrors = [];
    helpPage.on('console', message => {
      if (message.type() === 'error') browserErrors.push(message.text());
    });
    helpPage.on('pageerror', error => browserErrors.push(error.message));
    await helpPage.goto(`${baseUrl}/help`, { waitUntil: 'networkidle' });
    if ((await helpPage.locator('h1').textContent())?.trim() !== '先找到正确的操作手册') throw new Error('Help home did not render.');
    if (await helpPage.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)) throw new Error(`Help ${viewport.name} layout overflows horizontally.`);
    if (viewport.name === 'mobile') {
      await helpPage.getByRole('button', { name: '目录' }).click();
      await helpPage.getByLabel('搜索当前文档').waitFor();
    }
    await helpPage.getByLabel('搜索当前文档').fill('部署');
    await helpPage.getByRole('link', { name: '部署、反向代理与 TLS' }).waitFor();
    await helpPage.screenshot({ path: `output/playwright/help-${viewport.name}.png`, fullPage: true });
    await helpPage.getByRole('link', { name: '部署、反向代理与 TLS' }).first().click();
    await helpPage.getByRole('heading', { name: '部署、反向代理与 TLS', level: 1 }).waitFor();
    if (browserErrors.length) throw new Error(`Help ${viewport.name} browser errors: ${browserErrors.join('; ')}`);
    await helpPage.close();
  }
} finally {
  await browser.close();
}
