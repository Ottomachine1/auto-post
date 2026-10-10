import {test,expect} from '@playwright/test';
test('Chinese translation keeps original accessible and labels machine output',async({page})=>{
 const item={id:'translation-ui-fixture',source:'demo',title:'Original market headline',body:'Original market report.',chineseTitle:'市场新闻中文标题',chineseBody:'市场报道的中文译文。',translationStatus:'completed',translationEngine:'argos-offline',category:'宏观',published_at:1791640000,collected_at:1791640001,demo:false,groupId:'translation-ui-fixture',analysisStatus:'pending',language:'en'};
 await page.route('**/api/events/page*',route=>route.fulfill({json:{items:[item],nextCursor:null}}));
 await page.route('**/api/events/translation-ui-fixture',route=>route.fulfill({json:{item,analysis:null,related:[],drafts:[]}}));
 await page.goto('/');await page.getByRole('button',{name:'连接后端',exact:true}).click();await page.getByLabel('管理访问令牌').fill('local-preview-token-at-least-32-characters');await page.getByRole('button',{name:'验证并连接'}).click();
 await expect(page.locator('.event h3')).toHaveText('市场新闻中文标题');
 await expect(page.locator('.event')).toContainText('中文机译');
 await page.locator('.event-main').click();
 await expect(page.getByText('本地机器翻译 · 请核对原文',{exact:true})).toBeVisible();
 await page.getByText('查看原文',{exact:true}).click();
 await expect(page.getByText('Original market report.',{exact:true})).toBeVisible();
 await page.getByRole('button',{name:'返回事件列表'}).click();
 await page.getByLabel('搜索事件').fill('中文标题');
 await expect(page.locator('.event h3')).toHaveCount(1);
});
