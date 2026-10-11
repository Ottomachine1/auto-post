import {test,expect} from '@playwright/test';
test('analytics uses authenticated range queries and exposes numeric trends',async({page})=>{
 await page.goto('/');await page.getByRole('button',{name:'连接后端',exact:true}).click();await page.getByLabel('管理访问令牌').fill('local-preview-token-at-least-32-characters');await page.getByRole('button',{name:'验证并连接'}).click();await expect(page.getByText('实时连接',{exact:true}).first()).toBeVisible();
 await page.getByRole('button',{name:'情报统计',exact:true}).click();
 await expect(page.getByRole('heading',{name:'采样报道趋势',exact:true})).toBeVisible();
 await expect(page.getByText(/情绪指标尚不可用/)).toBeVisible();
 const request=page.waitForResponse(r=>r.url().includes('/api/analytics?range=1h'));
 await page.getByLabel('统计时间范围').selectOption('1h');const response=await request;expect(response.status()).toBe(200);const data=await response.json();expect(data.window.to-data.window.from).toBe(3600);expect(data.timeline.reduce((n:number,b:{reports:number})=>n+b.reports,0)).toBe(data.reports);
 await page.getByText('查看趋势数值',{exact:true}).click();await expect(page.getByRole('columnheader',{name:'分桶开始'})).toBeVisible();
 await page.getByLabel('统计时间范围').selectOption('custom');await page.getByLabel('统计开始时间').fill('2020-01-01T00:00');await page.getByLabel('统计结束时间').fill('2020-01-01T01:00');await page.getByRole('button',{name:'应用范围',exact:true}).click();await expect(page.getByText('此范围没有采样记录。').first()).toBeVisible();
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});
