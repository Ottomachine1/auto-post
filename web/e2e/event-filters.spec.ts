import {test,expect} from '@playwright/test';
test('advanced event filters use readonly score snapshots and reset cleanly',async({page})=>{
 await page.goto('/');await page.getByRole('button',{name:'连接后端',exact:true}).click();await page.getByLabel('管理访问令牌').fill('local-preview-token-at-least-32-characters');await page.getByRole('button',{name:'验证并连接'}).click();await expect(page.locator('.value-score').first()).toHaveText(/重要性 \d+\/100/);
 await page.getByText('高级筛选 · 时间、语言、重要性',{exact:true}).click();
 let scoreWrites=0;page.on('request',r=>{if(r.url().endsWith('/api/events/scores')&&r.method()==='POST')scoreWrites++;});
 await page.getByLabel('最低重要性',{exact:true}).fill('80');await page.getByLabel('最高重要性',{exact:true}).fill('100');
 const filtered=page.waitForResponse(r=>r.url().includes('/api/events/page?')&&r.url().includes('minScore=80'));
 await page.getByRole('button',{name:'应用情报筛选',exact:true}).click();expect((await filtered).status()).toBe(200);
 await page.getByLabel('评分状态',{exact:true}).selectOption('unscored');await expect(page.getByLabel('最低重要性',{exact:true})).toBeDisabled();
 const missing=page.waitForResponse(r=>r.url().includes('/api/events/page?')&&r.url().includes('scoreState=unscored'));
 await page.getByRole('button',{name:'应用情报筛选',exact:true}).click();const response=await missing;expect(response.status()).toBe(200);expect(response.url()).not.toContain('minScore');
 await expect(page.locator('.value-score').filter({hasText:'计算中'})).toHaveCount(0);expect(scoreWrites).toBe(0);
 const cleared=page.waitForResponse(r=>r.url().includes('/api/events/page?')&&!r.url().includes('scoreState=')&&!r.url().includes('minScore='));await page.getByRole('button',{name:'清除高级筛选',exact:true}).click();expect((await cleared).status()).toBe(200);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});
