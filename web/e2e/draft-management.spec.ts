import {test,expect} from '@playwright/test';
test('Draft trash confirmation and restoration preserve review boundary',async({page})=>{
 const text='Lifecycle browser fixture '+Date.now();
 await page.goto('/');await page.getByRole('button',{name:'连接后端',exact:true}).click();await page.getByLabel('管理访问令牌').fill('local-preview-token-at-least-32-characters');await page.getByRole('button',{name:'验证并连接'}).click();
 await expect(page.getByText('实时连接',{exact:true}).first()).toBeVisible();
 await page.getByRole('button',{name:'创建内容',exact:true}).click();await page.getByLabel('推文内容',{exact:true}).fill(text);await page.getByRole('button',{name:'保存待审核草稿',exact:true}).click();
 await page.getByLabel('搜索草稿').fill(text);
 const card=page.locator('article').filter({has:page.getByText(text,{exact:true})});
 await expect(card).toHaveCount(1);
 await card.getByRole('button',{name:'审核当前版本',exact:true}).click();await expect(card.getByText('已审核',{exact:true})).toBeVisible();
 await card.getByRole('button',{name:'移入回收站',exact:true}).click();
 const confirmation=page.getByRole('alertdialog');await expect(confirmation).toContainText('1 条草稿');await confirmation.getByRole('button',{name:'取消',exact:true}).click();
 await expect(card).toBeVisible();await card.getByRole('button',{name:'移入回收站',exact:true}).click();await confirmation.getByRole('button',{name:'确认移入回收站',exact:true}).click();await expect(card).toHaveCount(0);
 await page.getByRole('navigation',{name:'草稿分类'}).getByRole('button',{name:'回收站',exact:true}).click();await expect(card).toHaveCount(1);
 await card.getByRole('button',{name:'恢复草稿',exact:true}).click();await confirmation.getByRole('button',{name:'确认恢复草稿',exact:true}).click();await expect(card).toHaveCount(0);
 await page.getByRole('navigation',{name:'草稿分类'}).getByRole('button',{name:'发布中心',exact:true}).click();await expect(card).toHaveCount(1);await expect(card.getByText('待审核',{exact:true})).toBeVisible();await expect(card.getByRole('button',{name:'提交发布',exact:true})).toHaveCount(0);
});
