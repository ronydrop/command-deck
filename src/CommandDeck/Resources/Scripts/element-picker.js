(function(){
'use strict';
if(window.__EP){window.__EP.destroy();}

const EP={
  active:false,current:null,overlay:null,observers:[],

  createOverlay(){
    const c=document.createElement('div');
    c.id='__ep-root';
    c.setAttribute('data-ep','1');

    const hl=document.createElement('div');
    hl.id='__ep-hl';
    Object.assign(hl.style,{
      position:'fixed',pointerEvents:'none',zIndex:'2147483647',
      border:'2px solid #1a73e8',backgroundColor:'rgba(26,115,232,0.12)',
      transition:'all 50ms ease-out',display:'none',boxSizing:'border-box'
    });

    const mg=document.createElement('div');
    mg.id='__ep-mg';
    Object.assign(mg.style,{
      position:'fixed',pointerEvents:'none',zIndex:'2147483646',
      backgroundColor:'rgba(246,178,107,0.25)',display:'none',boxSizing:'border-box'
    });

    const pd=document.createElement('div');
    pd.id='__ep-pd';
    Object.assign(pd.style,{
      position:'fixed',pointerEvents:'none',zIndex:'2147483646',
      backgroundColor:'rgba(147,196,125,0.3)',display:'none',boxSizing:'border-box'
    });

    const tt=document.createElement('div');
    tt.id='__ep-tt';
    Object.assign(tt.style,{
      position:'fixed',pointerEvents:'none',zIndex:'2147483647',
      backgroundColor:'#1e1e2e',color:'#cdd6f4',padding:'4px 8px',
      borderRadius:'4px',fontSize:'11px',
      fontFamily:'Consolas,monospace',lineHeight:'1.4',
      whiteSpace:'nowrap',display:'none',
      boxShadow:'0 2px 8px rgba(0,0,0,0.4)',maxWidth:'500px',
      overflow:'hidden',textOverflow:'ellipsis'
    });

    c.appendChild(mg);c.appendChild(pd);c.appendChild(hl);c.appendChild(tt);
    document.documentElement.appendChild(c);
    this.overlay={container:c,hl,mg,pd,tt};
    this._protectOverlay();
  },

  _protectOverlay(){
    const obs=new MutationObserver(muts=>{
      for(const m of muts)
        for(const r of m.removedNodes)
          if(r===this.overlay?.container||r.id==='__ep-root')
            document.documentElement.appendChild(this.overlay.container);
    });
    obs.observe(document.documentElement,{childList:true});
    this.observers.push(obs);
  },

  _isOurs(el){return el&&el.closest&&el.closest('#__ep-root')!==null;},

  getBoxInfo(el){
    const r=el.getBoundingClientRect();
    const cs=getComputedStyle(el);
    const pf=v=>parseFloat(v)||0;
    const margin={t:pf(cs.marginTop),r:pf(cs.marginRight),b:pf(cs.marginBottom),l:pf(cs.marginLeft)};
    const padding={t:pf(cs.paddingTop),r:pf(cs.paddingRight),b:pf(cs.paddingBottom),l:pf(cs.paddingLeft)};
    const border={t:pf(cs.borderTopWidth),r:pf(cs.borderRightWidth),b:pf(cs.borderBottomWidth),l:pf(cs.borderLeftWidth)};
    return{
      border:{x:r.left,y:r.top,w:r.width,h:r.height},
      margin:{x:r.left-margin.l,y:r.top-margin.t,w:r.width+margin.l+margin.r,h:r.height+margin.t+margin.b},
      padding:{x:r.left+border.l,y:r.top+border.t,w:r.width-border.l-border.r,h:r.height-border.t-border.b},
      abs:{x:r.left+scrollX,y:r.top+scrollY,w:r.width,h:r.height},
      marginV:margin,paddingV:padding,borderV:border
    };
  },

  posBox(el,s,b){Object.assign(el.style,{display:'block',left:b.x+'px',top:b.y+'px',width:b.w+'px',height:b.h+'px'});},

  updateOverlay(el){
    if(!el||!this.overlay)return;
    const bi=this.getBoxInfo(el);
    this.posBox(this.overlay.hl,this.overlay.hl.style,bi.border);
    this.posBox(this.overlay.mg,this.overlay.mg.style,bi.margin);
    const hp=bi.paddingV.t||bi.paddingV.r||bi.paddingV.b||bi.paddingV.l;
    if(hp)this.posBox(this.overlay.pd,this.overlay.pd.style,bi.padding);
    else this.overlay.pd.style.display='none';
    this.updateTooltip(el,bi);
  },

  updateTooltip(el,bi){
    const tt=this.overlay.tt;
    const tag=el.tagName.toLowerCase();
    const id=el.id?'#'+el.id:'';
    const cls=el.className&&typeof el.className==='string'?'.'+el.className.trim().split(/\s+/).slice(0,3).join('.'):'';
    const dims=Math.round(bi.border.w)+'×'+Math.round(bi.border.h);
    const testId=el.getAttribute('data-testid')||el.getAttribute('data-cy')||'';
    const extra=testId?' ['+testId+']':'';

    tt.innerHTML='';
    const sp=(color,text)=>{const s=document.createElement('span');s.style.color=color;s.textContent=text;tt.appendChild(s);};
    sp('#f38ba8',tag);
    if(id)sp('#fab387',id);
    if(cls&&cls!=='.')sp('#a6e3a1',cls.substring(0,50));
    sp('#6c7086',' | '+dims+extra);

    tt.style.display='block';
    let ty=bi.border.y-28;if(ty<0)ty=bi.border.y+bi.border.h+6;
    tt.style.left=Math.max(4,Math.min(bi.border.x,innerWidth-350))+'px';
    tt.style.top=Math.max(4,ty)+'px';
  },

  hideOverlay(){
    if(!this.overlay)return;
    ['hl','mg','pd','tt'].forEach(k=>this.overlay[k].style.display='none');
  },

  // === Selector generation ===
  getSelector(el){
    const tid=this._testIdSel(el);if(tid&&this._unique(tid))return tid;
    if(el.id&&this._validId(el.id)){const s='#'+CSS.escape(el.id);if(this._unique(s))return s;}
    const cls=this._classSel(el);if(cls&&this._unique(cls))return cls;
    return this._nthPath(el);
  },
  _testIdSel(el){
    for(const a of['data-testid','data-test-id','data-test','data-cy','data-qa']){
      const v=el.getAttribute(a);if(v)return'['+a+'="'+CSS.escape(v)+'"]';
    }return null;
  },
  _validId(id){return id&&!/^:r[0-9a-z]+:$/.test(id)&&!/^[a-f0-9]{8,}$/.test(id);},
  _classSel(el){
    if(!el.className||typeof el.className!=='string')return null;
    const classes=el.className.trim().split(/\s+/).filter(c=>c.length<40&&!/^(css-|sc-|_[a-z0-9]{5,}_)/.test(c));
    const tag=el.tagName.toLowerCase();
    for(const c of classes.slice(0,4)){const s=tag+'.'+CSS.escape(c);if(this._unique(s))return s;}
    return null;
  },
  _nthPath(el){
    const parts=[];let cur=el;
    while(cur&&cur.nodeType===1&&cur!==document.documentElement){
      const tag=cur.tagName.toLowerCase();const p=cur.parentElement;
      if(p){const i=Array.from(p.children).indexOf(cur)+1;parts.unshift(tag+':nth-child('+i+')');}
      else parts.unshift(tag);
      if(cur.id&&this._validId(cur.id)){parts[0]='#'+CSS.escape(cur.id);break;}
      cur=cur.parentElement;
    }
    return parts.join(' > ');
  },
  _unique(s){try{return document.querySelectorAll(s).length===1;}catch{return false;}},

  getXPath(el){
    if(el.id&&this._validId(el.id))return'//*[@id="'+el.id+'"]';
    const parts=[];let cur=el;
    while(cur&&cur.nodeType===1){
      let i=1;let sib=cur.previousElementSibling;
      while(sib){if(sib.tagName===cur.tagName)i++;sib=sib.previousElementSibling;}
      parts.unshift(cur.tagName.toLowerCase()+'['+i+']');cur=cur.parentElement;
    }
    return'/'+parts.join('/');
  },

  // === Element serialization ===
  serialize(el){
    const bi=this.getBoxInfo(el);
    const attrs={};for(const a of el.attributes||[])attrs[a.name]=a.value.length>500?a.value.substring(0,500)+'…':a.value;
    const cs=getComputedStyle(el);
    const styles={};
    ['display','position','visibility','opacity','width','height','flexDirection','justifyContent',
     'alignItems','gap','overflow','fontSize','fontFamily','fontWeight','color','backgroundColor',
     'borderRadius','zIndex','cursor','transform','gridTemplateColumns'].forEach(p=>{
      const v=cs[p];if(v&&v!=='none'&&v!=='normal'&&v!=='auto'&&v!=='0px')styles[p]=v;
    });

    const ancestors=[];let anc=el.parentElement;let d=0;
    while(anc&&anc!==document.documentElement&&d<10){
      ancestors.push({tagName:anc.tagName.toLowerCase(),id:anc.id||null,
        className:typeof anc.className==='string'?anc.className.trim().substring(0,80):'',
        role:anc.getAttribute('role')});
      anc=anc.parentElement;d++;
    }

    const children=Array.from(el.children||[]).slice(0,20).map(c=>({
      tagName:c.tagName.toLowerCase(),id:c.id||null,
      className:typeof c.className==='string'?c.className.trim().substring(0,50):'',
      textPreview:(c.textContent||'').trim().substring(0,40)
    }));

    const fw={framework:null,componentName:null,componentStack:[],testIds:{}};
    for(const a of['data-testid','data-test-id','data-cy']){const v=el.getAttribute(a);if(v)fw.testIds[a]=v;}
    const rfKey=Object.keys(el).find(k=>k.startsWith('__reactFiber$')||k.startsWith('__reactInternalInstance$'));
    if(rfKey){
      fw.framework='react';
      try{
        let f=el[rfKey];
        if(f?.type){fw.componentName=typeof f.type==='function'?(f.type.displayName||f.type.name):f.type;}
        let depth=0;while(f&&depth<6){
          if(f.type&&typeof f.type==='function')fw.componentStack.push(f.type.displayName||f.type.name||'Anonymous');
          f=f.return;depth++;
        }
      }catch{}
    }
    if(el.__vue__||el.__vue_app__){fw.framework='vue';try{fw.componentName=el.__vue__?.$options?.name;}catch{}}

    return{
      tagName:el.tagName.toLowerCase(),
      id:el.id||null,
      className:typeof el.className==='string'?el.className:'',
      cssSelector:this.getSelector(el),
      xpath:this.getXPath(el),
      textContent:(el.textContent||'').trim().substring(0,500),
      innerText:(el.innerText||'').trim().substring(0,500),
      innerHTML:(el.innerHTML||'').substring(0,1500),
      outerHTML:(el.outerHTML||'').substring(0,3000),
      attributes:attrs,
      boundingBox:bi.border,
      absolutePosition:bi.abs,
      boxModel:{margin:bi.marginV,padding:bi.paddingV,border:bi.borderV},
      computedStyles:styles,
      ancestors:ancestors,
      childrenSummary:{count:el.children.length,tags:children,hasMore:el.children.length>20},
      accessibility:{
        role:el.getAttribute('role'),ariaLabel:el.getAttribute('aria-label'),
        tabIndex:el.tabIndex,title:el.title||null,alt:el.getAttribute('alt'),
        placeholder:el.getAttribute('placeholder'),name:el.getAttribute('name'),
        type:el.getAttribute('type'),value:el.value!==undefined?String(el.value).substring(0,200):null
      },
      frameworkInfo:fw,
      viewport:{width:innerWidth,height:innerHeight,scrollX:scrollX,scrollY:scrollY},
      url:location.href,
      timestamp:Date.now()
    };
  },

  // === Event Handlers ===
  onMove(e){
    if(this.overlay)this.overlay.container.style.display='none';
    let t=document.elementFromPoint(e.clientX,e.clientY);
    if(this.overlay)this.overlay.container.style.display='';
    if(!t||this._isOurs(t))return;
    if(t.shadowRoot){const st=t.shadowRoot.elementFromPoint(e.clientX,e.clientY);if(st&&!this._isOurs(st))t=st;}
    if(t!==this.current){this.current=t;this.updateOverlay(t);}
  },

  onClick(e){
    e.preventDefault();e.stopPropagation();e.stopImmediatePropagation();
    if(!this.current)return;
    const data=this.serialize(this.current);
    this.send('elementSelected',data);
    this.deactivate();
    return false;
  },

  onKey(e){
    if(e.key==='Escape'){this.deactivate();this.send('pickerCancelled',{});return;}
    if(!this.current)return;
    let nt=null;
    switch(e.key){
      case'ArrowUp':nt=this.current.parentElement;break;
      case'ArrowDown':nt=this.current.firstElementChild;break;
      case'ArrowLeft':nt=this.current.previousElementSibling;break;
      case'ArrowRight':nt=this.current.nextElementSibling;break;
      case'Enter':e.preventDefault();this.onClick(e);return;
    }
    if(nt&&!this._isOurs(nt)){e.preventDefault();this.current=nt;this.updateOverlay(nt);}
  },

  // === Activate / Deactivate ===
  activate(){
    if(this.active)return;
    this.active=true;
    this.createOverlay();
    this._h={
      move:e=>this.onMove(e),
      click:e=>this.onClick(e),
      key:e=>this.onKey(e),
      block:e=>{e.preventDefault();e.stopPropagation();},
      scroll:()=>{if(this.current)this.updateOverlay(this.current);},
    };
    document.addEventListener('mousemove',this._h.move,true);
    document.addEventListener('click',this._h.click,true);
    document.addEventListener('keydown',this._h.key,true);
    document.addEventListener('mousedown',this._h.block,true);
    document.addEventListener('mouseup',this._h.block,true);
    document.addEventListener('contextmenu',this._h.block,true);
    window.addEventListener('scroll',this._h.scroll,true);
    window.addEventListener('resize',this._h.scroll,true);
    document.body.style.cursor='crosshair';
    this.send('pickerActivated',{});
  },

  deactivate(){
    if(!this.active)return;
    this.active=false;
    document.removeEventListener('mousemove',this._h.move,true);
    document.removeEventListener('click',this._h.click,true);
    document.removeEventListener('keydown',this._h.key,true);
    document.removeEventListener('mousedown',this._h.block,true);
    document.removeEventListener('mouseup',this._h.block,true);
    document.removeEventListener('contextmenu',this._h.block,true);
    window.removeEventListener('scroll',this._h.scroll,true);
    window.removeEventListener('resize',this._h.scroll,true);
    this.observers.forEach(o=>o.disconnect());
    this.observers=[];
    if(this.overlay){this.overlay.container.remove();this.overlay=null;}
    document.body.style.cursor='';
    this.current=null;
    this.send('pickerDeactivated',{});
  },

  destroy(){this.deactivate();delete window.__EP;},

  send(type,data){
    const msg=JSON.stringify({type,data,source:'element-picker'});
    if(window.chrome?.webview?.postMessage)window.chrome.webview.postMessage(msg);
    window.dispatchEvent(new CustomEvent('ep-msg',{detail:{type,data}}));
  }
};

window.__EP=EP;
window.EPActivate=()=>EP.activate();
window.EPDeactivate=()=>EP.deactivate();
})();
