(() => {
  if(window.foamCompanionRuntime)return;window.foamCompanionRuntime=true;
  let revision=null,adRevision=null,adIndex=0,campaigns=[];
  function container() {return document.querySelector('.page-surface')||document.body}
  function renderAd() {
    document.getElementById('foam-sponsored')?.remove();
    if(!campaigns.length)return;
    const c=campaigns[adIndex++%campaigns.length],panel=document.createElement('aside');
    panel.id='foam-sponsored';panel.className='foam-sponsored';panel.dataset.pagefindIgnore='';panel.setAttribute('aria-label','Community message');
    const content=document.createElement(c.href?'a':'span');
    if(c.href){content.href='/_foam/go/'+encodeURIComponent(c.id);content.target='_blank';content.rel='sponsored noopener noreferrer';}
    if(c.image){const img=document.createElement('img');img.src='/_foam/ad-images/'+c.image;img.alt='';content.append(img)}
    const text=document.createElement('span');text.textContent=c.title+(c.text?' · '+c.text:'');content.append(text);content.title=text.textContent;
    panel.append(content);container().prepend(panel);
  }
  async function refresh() {
    try {
      const state=await (await fetch('/_foam/status.json',{cache:'no-store'})).json();
      if(revision!==null&&revision!==state.library&&!document.getElementById('foam-update-notice')) {
        const notice=document.createElement('div');notice.id='foam-update-notice';notice.className='foam-update-notice';notice.dataset.pagefindIgnore='';
        const text=document.createElement('span');text.textContent='Library updated.';
        const reload=document.createElement('button');reload.type='button';reload.textContent='Refresh';reload.onclick=()=>location.reload();
        notice.append(text,reload);container().prepend(notice);
      }
      revision=state.library;
      if(adRevision!==state.ads){campaigns=(await (await fetch('/_foam/campaigns.json',{cache:'no-store'})).json()).campaigns||[];adRevision=state.ads;renderAd()}
    }catch{/* Offline browsing continues with verified local content. */}
  }
  const start=()=>{refresh();setInterval(refresh,30000);setInterval(()=>{adRevision=null;refresh()},60000)};
  if(document.readyState==='complete')start();else window.addEventListener('load',start,{once:true});
})();
