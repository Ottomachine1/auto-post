import {test,expect} from '@playwright/test';
test('floating agent, session persistence and cancellation controls',async({page})=>{
 await page.goto('/');await page.getByRole('button',{name:'连接后端',exact:true}).click();await page.getByLabel('管理访问令牌').fill('local-preview-token-at-least-32-characters');await page.getByRole('button',{name:'验证并连接'}).click();
 await expect(page.getByText('实时连接',{exact:true}).first()).toBeVisible();
 await page.getByRole('button',{name:'打开情报 Agent',exact:true}).click();
 const panel=page.getByRole('dialog');await expect(panel.getByRole('heading',{name:'情报 Agent',exact:true})).toBeVisible();
 await panel.getByRole('navigation',{name:'Agent 快捷操作'}).getByRole('button',{name:'最新情报',exact:true}).click();
 await expect(panel.getByRole('button',{name:'停止任务'})).toBeVisible();
 await panel.getByRole('button',{name:'停止任务'}).click();await expect(panel.getByText('已停止',{exact:true})).toBeVisible();
 await panel.getByRole('button',{name:'会话历史',exact:true}).click();await expect(panel.getByRole('button',{name:/最新情报/})).toBeVisible();
 await panel.getByRole('button',{name:'关闭 Agent',exact:true}).click();await expect(page.getByRole('button',{name:'打开情报 Agent',exact:true})).toBeFocused();
});
