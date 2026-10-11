import {test,expect} from '@playwright/test';
test('Scores show reasons and persist explicit feedback and weights',async({page})=>{
 await page.goto('/');await page.getByRole('button',{name:'连接后端',exact:true}).click();await page.getByLabel('管理访问令牌').fill('local-preview-token-at-least-32-characters');await page.getByRole('button',{name:'验证并连接'}).click();
 await expect(page.getByText('实时连接',{exact:true}).first()).toBeVisible();
 await expect(page.locator('.value-score').first()).toHaveText(/重要性 \d+\/100/);
 await page.locator('.event-main').first().click();
 const panel=page.getByRole('region',{name:'重要性评分依据'});await expect(panel).toContainText('value-v1');await expect(panel).toContainText('网站流量未知');
 await panel.getByText('提供人工评价',{exact:true}).click();await panel.getByLabel('这条信息的价值（0–100）').fill('83');await panel.getByLabel('评分反馈说明').fill('Browser verified feedback');const saved=page.waitForResponse(r=>r.url().endsWith('/api/scoring/feedback')&&r.request().method()==='POST');await panel.getByRole('button',{name:'保存评分反馈',exact:true}).click();expect((await saved).ok()).toBeTruthy();
 await expect.poll(async()=>page.request.get('/api/scoring/feedback/dotnet-demo-0').then(async r=>{const text=await r.text();return text?JSON.parse(text)?.rating:null;})).toBe(83);
 const back=page.getByRole('button',{name:'返回事件列表',exact:true});if(await back.isVisible())await back.click();
 await page.getByRole('button',{name:'系统状态',exact:true}).click();const settings=page.getByRole('region',{name:'重要性评分设置'});await expect(settings).toContainText('合计 100%');
 await settings.getByLabel('来源可信度权重').fill('24');await expect(settings.getByRole('button',{name:'保存权重',exact:true})).toBeDisabled();await settings.getByLabel('来源可信度权重').fill('25');await expect(settings.getByRole('button',{name:'保存权重',exact:true})).toBeEnabled();
});
