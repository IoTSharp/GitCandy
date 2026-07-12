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
  }
} finally {
  await browser.close();
}
