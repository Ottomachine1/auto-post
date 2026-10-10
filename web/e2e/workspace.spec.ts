import { test, expect } from "@playwright/test";
test("login, stream, draft revision and responsive navigation", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByRole("button", { name: "连接后端", exact: true }).click();
  await page
    .getByLabel("管理访问令牌")
    .fill("local-preview-token-at-least-32-characters");
  await page.getByRole("button", { name: "验证并连接" }).click();
  await expect(
    page.getByText("实时连接", { exact: true }).first(),
  ).toBeVisible();
  await page.getByRole("button", { name: "创建内容", exact: true }).click();
  const text = "UI acceptance " + Date.now();
  await page.getByRole("textbox", { name: "内容", exact: true }).fill(text);
  await page.getByRole("button", { name: "分渠道预览", exact: true }).click();
  await expect(page.getByText("通过内容校验")).toBeVisible();
  await page
    .getByRole("button", { name: "保存待审核草稿", exact: true })
    .click();
  await expect(page.getByText(text, { exact: true })).toBeVisible();
  const card = page.locator(".content-card").filter({ hasText: text });
  await card.getByRole("button", { name: "审核当前版本" }).click();
  await expect(card.getByText("已审核", { exact: true })).toBeVisible();
  await card.getByRole("button", { name: "编辑", exact: true }).click();
  await page
    .getByRole("textbox", { name: "内容", exact: true })
    .fill(text + " v2");
  await page
    .getByRole("button", { name: "保存待审核草稿", exact: true })
    .click();
  await expect(page.getByText(text + " v2", { exact: true })).toBeVisible();
  await expect(
    page
      .locator(".content-card")
      .filter({ hasText: text + " v2" })
      .getByText("待审核", { exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "实时情报", exact: false }).click();
  await page.getByLabel("搜索事件").fill("比特币");
  await expect(page.locator(".event h3")).toHaveCount(1);
  await page.locator(".event-main").click();
  await expect(
    page.getByRole("button", { name: "返回事件列表" }),
  ).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBeTruthy();
});
