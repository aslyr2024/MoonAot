import{b as ae,a4 as ue,a5 as be,l,j as h,q as W,T as ze,h as Xe,a6 as O,n as F,a7 as U,a8 as Se,a9 as ne,aa as fe,ab as Ye,r as $,ac as xe,ad as me,e as Be,ae as Ve,af as qe,w as Ee,i as Ke,ag as Je,f as p,ah as Ge,ai as se,aj as Qe,ak as Ze,al as et,am as c,an as le,ao as D,ap as A,aq as Y,ar as tt,u as Re,as as ot,at as rt,au as at,av as st,aw as nt,ax as V,ay as it,az as lt,aA as ge,m as o,F as pe,aB as dt,aC as ct,g as ut,E as ye,K as ht,z as n,aD as ie,C as y,aE as oe,x as b,B as J,A as L,R as Z,V as ee,t as re,aF as he,aG as Fe,aH as Te,y as S,D as q,aI as vt,aJ as ft,aK as mt,J as bt,a1 as gt,N as K,Q as pt,O as we}from"./index-DhzlqByB.js";import{H as ve}from"./heart-C1Z6a44N.js";import{T as yt}from"./triangle-alert-DnqMTrQn.js";import{u as ke,I as Ce}from"./Input-D1nUOlir.js";import{f as $e}from"./format-length-B-p6aW7q.js";import{S as wt}from"./Spin-rfr0GMbW.js";import{I as kt}from"./image-plus-QecKvuyt.js";import{S as Ct}from"./save-jkBYtMEd.js";import{C as $t}from"./copy-DCHiiDxk.js";import{T as zt}from"./trash-2-6JE_oc_-.js";const St=["onMouseenter","onMouseleave","onMousedown"],xt={key:1,role:"none"};var Bt=ae({name:"NDrawerContent",inheritAttrs:!1,props:{blockScroll:Boolean,show:{type:Boolean,default:void 0},displayDirective:{type:String,required:!0},placement:{type:String,required:!0},contentClass:String,contentStyle:[Object,String],nativeScrollbar:{type:Boolean,required:!0},scrollbarProps:Object,trapFocus:{type:Boolean,default:!0},autoFocus:{type:Boolean,default:!0},showMask:{type:[Boolean,String],required:!0},maxWidth:Number,maxHeight:Number,minWidth:Number,minHeight:Number,resizable:Boolean,onClickoutside:Function,onAfterLeave:Function,onAfterEnter:Function,onEsc:Function},setup(e){const s=$(!!e.show),r=$(null),v=xe(me);let t=0,x="",g=null;const k=$(!1),w=$(!1),T=p(()=>e.placement==="top"||e.placement==="bottom"),{mergedClsPrefixRef:I,mergedRtlRef:E}=Be(e),R=Ve("Drawer",E,I),M=i,B=u=>{w.value=!0,t=T.value?u.clientY:u.clientX,x=document.body.style.cursor,document.body.style.cursor=T.value?"ns-resize":"ew-resize",document.body.addEventListener("mousemove",X),document.body.addEventListener("mouseleave",M),document.body.addEventListener("mouseup",i)},_=()=>{g!==null&&(window.clearTimeout(g),g=null),w.value?k.value=!0:g=window.setTimeout(()=>{k.value=!0},300)},N=()=>{g!==null&&(window.clearTimeout(g),g=null),k.value=!1},{doUpdateHeight:f,doUpdateWidth:P}=v,j=u=>{const{maxWidth:m}=e;if(m&&u>m)return m;const{minWidth:d}=e;return d&&u<d?d:u},G=u=>{const{maxHeight:m}=e;if(m&&u>m)return m;const{minHeight:d}=e;return d&&u<d?d:u};function X(u){if(w.value)if(T.value){let m=r.value?.offsetHeight||0;const d=t-u.clientY;m+=e.placement==="bottom"?d:-d,m=G(m),f(m),t=u.clientY}else{let m=r.value?.offsetWidth||0;const d=t-u.clientX;m+=e.placement==="right"?d:-d,m=j(m),P(m),t=u.clientX}}function i(){w.value&&(t=0,w.value=!1,document.body.style.cursor=x,document.body.removeEventListener("mousemove",X),document.body.removeEventListener("mouseup",i),document.body.removeEventListener("mouseleave",M))}qe(()=>{e.show&&(s.value=!0)}),Ee(()=>e.show,u=>{u||i()}),Ke(()=>{i()});const C=p(()=>{const{show:u}=e,m=[[be,u]];return e.showMask||m.push([Ge,e.onClickoutside,void 0,{capture:!0}]),m});function z(){s.value=!1,e.onAfterLeave?.()}return Je(p(()=>e.blockScroll&&s.value)),se(Qe,r),se(Ze,null),se(et,null),{bodyRef:r,rtlEnabled:R,mergedClsPrefix:v.mergedClsPrefixRef,isMounted:v.isMountedRef,mergedTheme:v.mergedThemeRef,displayed:s,transitionName:p(()=>({right:"slide-in-from-right-transition",left:"slide-in-from-left-transition",top:"slide-in-from-top-transition",bottom:"slide-in-from-bottom-transition"})[e.placement]),handleAfterLeave:z,bodyDirectives:C,handleMousedownResizeTrigger:B,handleMouseenterResizeTrigger:_,handleMouseleaveResizeTrigger:N,isDragging:w,isHoverOnResizeTrigger:k}},render(){const{$slots:e,mergedClsPrefix:s}=this;return this.displayDirective==="show"||this.displayed||this.show?ue((l(),h("div",xt,[(l(),W(Ye,{disabled:!this.showMask||!this.trapFocus,active:this.show,autoFocus:this.autoFocus,onEsc:this.onEsc},{default:()=>(l(),W(ze,{name:this.transitionName,appear:this.isMounted,onAfterEnter:this.onAfterEnter,onAfterLeave:this.handleAfterLeave},{default:()=>ue(Xe("div",ne(this.$attrs,{role:"dialog",ref:"bodyRef","aria-modal":"true",class:[`${s}-drawer`,this.rtlEnabled&&`${s}-drawer--rtl`,`${s}-drawer--${this.placement}-placement`,this.isDragging&&`${s}-drawer--unselectable`,this.nativeScrollbar&&`${s}-drawer--native-scrollbar`]}),[this.resizable?(l(),h("div",{key:2,class:O([`${s}-drawer__resize-trigger`,(this.isDragging||this.isHoverOnResizeTrigger)&&`${s}-drawer__resize-trigger--hover`]),onMouseenter:this.handleMouseenterResizeTrigger,onMouseleave:this.handleMouseleaveResizeTrigger,onMousedown:this.handleMousedownResizeTrigger},null,42,St)):null,this.nativeScrollbar?(l(),h("div",{key:3,class:O([`${s}-drawer-content-wrapper`,this.contentClass]),style:F(this.contentStyle),role:"none"},[U(()=>e.default?.())],6)):(l(),W(Se,ne({key:4},this.scrollbarProps,{contentStyle:this.contentStyle,contentClass:[`${s}-drawer-content-wrapper`,this.contentClass],theme:this.mergedTheme.peers.Scrollbar,themeOverrides:this.mergedTheme.peerOverrides.Scrollbar}),fe(e),1040,["contentStyle","contentClass","theme","themeOverrides"]))]),this.bodyDirectives)},1032,["name","appear","onAfterEnter","onAfterLeave"]))},1032,["disabled","active","autoFocus","onEsc"]))])),[[be,this.displayDirective==="if"||this.displayed||this.show]]):null}});const{cubicBezierEaseIn:Et,cubicBezierEaseOut:Rt}=le;function Ft({duration:e="0.3s",leaveDuration:s="0.2s",name:r="slide-in-from-bottom"}={}){return[c(`&.${r}-transition-leave-active`,{transition:`transform ${s} ${Et}`}),c(`&.${r}-transition-enter-active`,{transition:`transform ${e} ${Rt}`}),c(`&.${r}-transition-enter-to`,{transform:"translateY(0)"}),c(`&.${r}-transition-enter-from`,{transform:"translateY(100%)"}),c(`&.${r}-transition-leave-from`,{transform:"translateY(0)"}),c(`&.${r}-transition-leave-to`,{transform:"translateY(100%)"})]}const{cubicBezierEaseIn:Tt,cubicBezierEaseOut:Mt}=le;function Pt({duration:e="0.3s",leaveDuration:s="0.2s",name:r="slide-in-from-left"}={}){return[c(`&.${r}-transition-leave-active`,{transition:`transform ${s} ${Tt}`}),c(`&.${r}-transition-enter-active`,{transition:`transform ${e} ${Mt}`}),c(`&.${r}-transition-enter-to`,{transform:"translateX(0)"}),c(`&.${r}-transition-enter-from`,{transform:"translateX(-100%)"}),c(`&.${r}-transition-leave-from`,{transform:"translateX(0)"}),c(`&.${r}-transition-leave-to`,{transform:"translateX(-100%)"})]}const{cubicBezierEaseIn:Dt,cubicBezierEaseOut:Ht}=le;function Ot({duration:e="0.3s",leaveDuration:s="0.2s",name:r="slide-in-from-right"}={}){return[c(`&.${r}-transition-leave-active`,{transition:`transform ${s} ${Dt}`}),c(`&.${r}-transition-enter-active`,{transition:`transform ${e} ${Ht}`}),c(`&.${r}-transition-enter-to`,{transform:"translateX(0)"}),c(`&.${r}-transition-enter-from`,{transform:"translateX(100%)"}),c(`&.${r}-transition-leave-from`,{transform:"translateX(0)"}),c(`&.${r}-transition-leave-to`,{transform:"translateX(100%)"})]}const{cubicBezierEaseIn:It,cubicBezierEaseOut:_t}=le;function At({duration:e="0.3s",leaveDuration:s="0.2s",name:r="slide-in-from-top"}={}){return[c(`&.${r}-transition-leave-active`,{transition:`transform ${s} ${It}`}),c(`&.${r}-transition-enter-active`,{transition:`transform ${e} ${_t}`}),c(`&.${r}-transition-enter-to`,{transform:"translateY(0)"}),c(`&.${r}-transition-enter-from`,{transform:"translateY(-100%)"}),c(`&.${r}-transition-leave-from`,{transform:"translateY(0)"}),c(`&.${r}-transition-leave-to`,{transform:"translateY(-100%)"})]}var Ut=c([D("drawer",`
 word-break: break-word;
 line-height: var(--n-line-height);
 position: absolute;
 pointer-events: all;
 box-shadow: var(--n-box-shadow);
 transition:
 background-color .3s var(--n-bezier),
 color .3s var(--n-bezier);
 background-color: var(--n-color);
 color: var(--n-text-color);
 box-sizing: border-box;
 `,[Ot(),Pt(),At(),Ft(),A("unselectable",`
 user-select: none; 
 -webkit-user-select: none;
 `),A("native-scrollbar",[D("drawer-content-wrapper",`
 overflow: auto;
 height: 100%;
 `)]),Y("resize-trigger",`
 position: absolute;
 background-color: #0000;
 transition: background-color .3s var(--n-bezier);
 `,[A("hover",`
 background-color: var(--n-resize-trigger-color-hover);
 `)]),D("drawer-content-wrapper",`
 box-sizing: border-box;
 `),D("drawer-content",`
 height: 100%;
 display: flex;
 flex-direction: column;
 `,[A("native-scrollbar",[D("drawer-body-content-wrapper",`
 height: 100%;
 overflow: auto;
 `)]),D("drawer-body",`
 flex: 1 0 0;
 overflow: hidden;
 `),D("drawer-body-content-wrapper",`
 box-sizing: border-box;
 padding: var(--n-body-padding);
 `),D("drawer-header",`
 font-weight: var(--n-title-font-weight);
 line-height: 1;
 font-size: var(--n-title-font-size);
 color: var(--n-title-text-color);
 padding: var(--n-header-padding);
 transition: border .3s var(--n-bezier);
 border-bottom: 1px solid var(--n-divider-color);
 border-bottom: var(--n-header-border-bottom);
 display: flex;
 justify-content: space-between;
 align-items: center;
 `,[Y("main",`
 flex: 1;
 `),Y("close",`
 margin-left: 6px;
 transition:
 background-color .3s var(--n-bezier),
 color .3s var(--n-bezier);
 `)]),D("drawer-footer",`
 display: flex;
 justify-content: flex-end;
 border-top: var(--n-footer-border-top);
 transition: border .3s var(--n-bezier);
 padding: var(--n-footer-padding);
 `)]),A("right-placement",`
 top: 0;
 bottom: 0;
 right: 0;
 border-top-left-radius: var(--n-border-radius);
 border-bottom-left-radius: var(--n-border-radius);
 `,[Y("resize-trigger",`
 width: 3px;
 height: 100%;
 top: 0;
 left: 0;
 transform: translateX(-1.5px);
 cursor: ew-resize;
 `)]),A("left-placement",`
 top: 0;
 bottom: 0;
 left: 0;
 border-top-right-radius: var(--n-border-radius);
 border-bottom-right-radius: var(--n-border-radius);
 `,[Y("resize-trigger",`
 width: 3px;
 height: 100%;
 top: 0;
 right: 0;
 transform: translateX(1.5px);
 cursor: ew-resize;
 `)]),A("top-placement",`
 top: 0;
 left: 0;
 right: 0;
 border-bottom-left-radius: var(--n-border-radius);
 border-bottom-right-radius: var(--n-border-radius);
 `,[Y("resize-trigger",`
 width: 100%;
 height: 3px;
 bottom: 0;
 left: 0;
 transform: translateY(1.5px);
 cursor: ns-resize;
 `)]),A("bottom-placement",`
 left: 0;
 bottom: 0;
 right: 0;
 border-top-left-radius: var(--n-border-radius);
 border-top-right-radius: var(--n-border-radius);
 `,[Y("resize-trigger",`
 width: 100%;
 height: 3px;
 top: 0;
 left: 0;
 transform: translateY(-1.5px);
 cursor: ns-resize;
 `)])]),c("body",[c(">",[D("drawer-container",`
 position: fixed;
 `)])]),D("drawer-container",`
 position: relative;
 position: absolute;
 left: 0;
 right: 0;
 top: 0;
 bottom: 0;
 pointer-events: none;
 `,[c("> *",`
 pointer-events: all;
 `)]),D("drawer-mask",`
 background-color: rgba(0, 0, 0, .3);
 position: absolute;
 left: 0;
 right: 0;
 top: 0;
 bottom: 0;
 `,[A("invisible",`
 background-color: rgba(0, 0, 0, 0)
 `),tt({enterDuration:"0.2s",leaveDuration:"0.2s",enterCubicBezier:"var(--n-bezier-in)",leaveCubicBezier:"var(--n-bezier-out)"})])]);const Wt=["onClick"],Nt={...Re.props,show:Boolean,width:[Number,String],height:[Number,String],placement:{type:String,default:"right"},maskClosable:{type:Boolean,default:!0},showMask:{type:[Boolean,String],default:!0},to:[String,Object],displayDirective:{type:String,default:"if"},nativeScrollbar:{type:Boolean,default:!0},zIndex:Number,onMaskClick:Function,scrollbarProps:Object,contentClass:String,contentStyle:[Object,String],trapFocus:{type:Boolean,default:!0},onEsc:Function,autoFocus:{type:Boolean,default:!0},closeOnEsc:{type:Boolean,default:!0},blockScroll:{type:Boolean,default:!0},maxWidth:Number,maxHeight:Number,minWidth:Number,minHeight:Number,resizable:Boolean,defaultWidth:{type:[Number,String],default:251},defaultHeight:{type:[Number,String],default:251},onUpdateWidth:[Function,Array],onUpdateHeight:[Function,Array],"onUpdate:width":[Function,Array],"onUpdate:height":[Function,Array],"onUpdate:show":[Function,Array],onUpdateShow:[Function,Array],onAfterEnter:Function,onAfterLeave:Function,drawerStyle:[String,Object],drawerClass:String,target:null,onShow:Function,onHide:Function};var Lt=ae({name:"Drawer",inheritAttrs:!1,props:Nt,setup(e){const{mergedClsPrefixRef:s,namespaceRef:r,inlineThemeDisabled:v}=Be(e),t=rt(),x=Re("Drawer","-drawer",Ut,lt,e,s),g=$(e.defaultWidth),k=$(e.defaultHeight),w=ke(ge(e,"width"),g),T=ke(ge(e,"height"),k),I=p(()=>{const{placement:i}=e;return i==="top"||i==="bottom"?"":$e(w.value)}),E=p(()=>{const{placement:i}=e;return i==="left"||i==="right"?"":$e(T.value)}),R=i=>{const{onUpdateWidth:C,"onUpdate:width":z}=e;C&&V(C,i),z&&V(z,i),g.value=i},M=i=>{const{onUpdateHeight:C,"onUpdate:width":z}=e;C&&V(C,i),z&&V(z,i),k.value=i},B=p(()=>[{width:I.value,height:E.value},e.drawerStyle||""]);function _(i){const{onMaskClick:C,maskClosable:z}=e;z&&j(!1),C&&C(i)}function N(i){_(i)}const f=at();function P(i){e.onEsc?.(),e.show&&e.closeOnEsc&&nt(i)&&(f.value||j(!1))}function j(i){const{onHide:C,onUpdateShow:z,"onUpdate:show":u}=e;z&&V(z,i),u&&V(u,i),C&&!i&&V(C,i)}se(me,{isMountedRef:t,mergedThemeRef:x,mergedClsPrefixRef:s,doUpdateShow:j,doUpdateHeight:M,doUpdateWidth:R});const G=p(()=>{const{common:{cubicBezierEaseInOut:i,cubicBezierEaseIn:C,cubicBezierEaseOut:z},self:{color:u,textColor:m,boxShadow:d,lineHeight:a,headerPadding:H,footerPadding:te,borderRadius:de,bodyPadding:ce,titleFontSize:Q,titleTextColor:Me,titleFontWeight:Pe,headerBorderBottom:De,footerBorderTop:He,closeIconColor:Oe,closeIconColorHover:Ie,closeIconColorPressed:_e,closeColorHover:Ae,closeColorPressed:Ue,closeIconSize:We,closeSize:Ne,closeBorderRadius:Le,resizableTriggerColorHover:je}}=x.value;return{"--n-line-height":a,"--n-color":u,"--n-border-radius":de,"--n-text-color":m,"--n-box-shadow":d,"--n-bezier":i,"--n-bezier-out":z,"--n-bezier-in":C,"--n-header-padding":H,"--n-body-padding":ce,"--n-footer-padding":te,"--n-title-text-color":Me,"--n-title-font-size":Q,"--n-title-font-weight":Pe,"--n-header-border-bottom":De,"--n-footer-border-top":He,"--n-close-icon-color":Oe,"--n-close-icon-color-hover":Ie,"--n-close-icon-color-pressed":_e,"--n-close-size":Ne,"--n-close-color-hover":Ae,"--n-close-color-pressed":Ue,"--n-close-icon-size":We,"--n-close-border-radius":Le,"--n-resize-trigger-color-hover":je}}),X=v?st("drawer",void 0,G,e):void 0;return{mergedClsPrefix:s,namespace:r,mergedBodyStyle:B,handleOutsideClick:N,handleMaskClick:_,handleEsc:P,mergedTheme:x,cssVars:v?void 0:G,themeClass:X?.themeClass,onRender:X?.onRender,isMounted:t}},render(){const{mergedClsPrefix:e}=this;return l(),W(ot,{to:this.to,show:this.show},{default:()=>(this.onRender?.(),ue((l(),h("div",{class:O([`${e}-drawer-container`,this.namespace,this.themeClass]),style:F(this.cssVars),role:"none"},[this.showMask?(l(),W(ze,{key:0,name:"fade-in-transition",appear:this.isMounted},{default:()=>this.show?(l(),h("div",{key:1,"aria-hidden":!0,class:O([`${e}-drawer-mask`,this.showMask==="transparent"&&`${e}-drawer-mask--invisible`]),onClick:this.handleMaskClick},null,10,Wt)):null},1032,["appear"])):U(()=>null),(l(),W(Bt,ne(this.$attrs,{class:[this.drawerClass,this.$attrs.class],style:[this.mergedBodyStyle,this.$attrs.style],blockScroll:this.blockScroll,contentStyle:this.contentStyle,contentClass:this.contentClass,placement:this.placement,scrollbarProps:this.scrollbarProps,show:this.show,displayDirective:this.displayDirective,nativeScrollbar:this.nativeScrollbar,onAfterEnter:this.onAfterEnter,onAfterLeave:this.onAfterLeave,trapFocus:this.trapFocus,autoFocus:this.autoFocus,resizable:this.resizable,maxHeight:this.maxHeight,minHeight:this.minHeight,maxWidth:this.maxWidth,minWidth:this.minWidth,showMask:this.showMask,onEsc:this.handleEsc,onClickoutside:this.handleOutsideClick}),fe(this.$slots),1040,["class","style","blockScroll","contentStyle","contentClass","placement","scrollbarProps","show","displayDirective","nativeScrollbar","onAfterEnter","onAfterLeave","trapFocus","autoFocus","resizable","maxHeight","minHeight","maxWidth","minWidth","showMask","onEsc","onClickoutside"]))],6)),[[it,{zIndex:this.zIndex,enabled:this.show}]]))},1032,["to","show"])}});const jt={title:String,headerClass:String,headerStyle:[Object,String],footerClass:String,footerStyle:[Object,String],bodyClass:String,bodyStyle:[Object,String],bodyContentClass:String,bodyContentStyle:[Object,String],nativeScrollbar:{type:Boolean,default:!0},scrollbarProps:Object,closable:Boolean};var Xt=ae({name:"DrawerContent",props:jt,slots:Object,setup(){const e=xe(me,null);e||ct("drawer-content","`n-drawer-content` must be placed inside `n-drawer`.");const{doUpdateShow:s}=e;function r(){s(!1)}return{handleCloseClick:r,mergedTheme:e.mergedThemeRef,mergedClsPrefix:e.mergedClsPrefixRef}},render(){const{title:e,mergedClsPrefix:s,nativeScrollbar:r,mergedTheme:v,bodyClass:t,bodyStyle:x,bodyContentClass:g,bodyContentStyle:k,headerClass:w,headerStyle:T,footerClass:I,footerStyle:E,scrollbarProps:R,closable:M,$slots:B}=this;return l(),h("div",{role:"none",class:O([`${s}-drawer-content`,r&&`${s}-drawer-content--native-scrollbar`])},[B.header||e||M?(l(),h("div",{key:0,class:O([`${s}-drawer-header`,w]),style:F(T),role:"none"},[o("div",{class:O(`${s}-drawer-header__main`),role:"heading","aria-level":"1"},[B.header!==void 0?(l(),h(pe,{key:0},[U(()=>B.header())],64)):(l(),h(pe,{key:1},[U(()=>e)],64))],2),U(()=>M&&(l(),W(dt,{onClick:this.handleCloseClick,clsPrefix:s,class:O(`${s}-drawer-header__close`),absolute:!0},null,8,["onClick","clsPrefix","class"])))],6)):U(()=>null),r?(l(),h("div",{key:2,class:O([`${s}-drawer-body`,t]),style:F(x),role:"none"},[o("div",{class:O([`${s}-drawer-body-content-wrapper`,g]),style:F(k),role:"none"},[U(()=>B.default?.())],6)],6)):(l(),W(Se,ne({key:3,themeOverrides:v.peerOverrides.Scrollbar,theme:v.peers.Scrollbar},R,{class:`${s}-drawer-body`,contentClass:[`${s}-drawer-body-content-wrapper`,g],contentStyle:k}),fe(B),1040,["themeOverrides","theme","class","contentClass","contentStyle"])),B.footer?(l(),h("div",{key:4,class:O([`${s}-drawer-footer`,I]),style:F(E),role:"none"},[U(()=>B.footer())],6)):U(()=>null)],2)}});/**
 * @license lucide-vue-next v0.468.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Yt=ut("EllipsisIcon",[["circle",{cx:"12",cy:"12",r:"1",key:"41hilf"}],["circle",{cx:"19",cy:"12",r:"1",key:"1wjl8i"}],["circle",{cx:"5",cy:"12",r:"1",key:"1pcz8c"}]]),Vt=["src"],qt=["src"],Kt={class:"ph-format"},Jt={class:"ph-title"},Gt={class:"ph-author"},Qt={key:3,class:"fav-mark",title:"已收藏"},Zt={key:4,class:"miss-mark",title:"文件缺失"},eo={class:"cover-actions"},to={key:5,class:"cover-progress"},oo={key:6,class:"pct-badge"},ro={class:"book-meta"},ao=["title"],so={class:"book-row"},no=["title"],io={class:"fmt-chip"},lo={class:"book-bar"},No=ae({__name:"BookCard",props:{item:{},width:{}},emits:["open","menu","favorite"],setup(e,{emit:s}){const r=e,v=s,t=$(!1),x=$(!1),g=$(!1),k=p(()=>Fe(r.item.format)),w=p(()=>re.settings.showCovers&&r.item.hasCover),T=p(()=>k.value&&!r.item.hasCover&&re.settings.useDefaultCover&&!!re.settings.defaultCoverPath),I=p(()=>ie.thumb(r.item)),E=p(()=>he(r.item.progress)),R=p(()=>Te(r.item.hash||r.item.title)),M=p(()=>({background:`radial-gradient(120% 100% at 20% 0%, hsl(${R.value} 42% 30%), hsl(${(R.value+46)%360} 36% 16%) 70%)`}));function B(){t.value=!0,g.value=!0}async function _(N){N.stopPropagation();try{const f=!r.item.favorite;await Z.favorite(r.item.hash,f),v("favorite",r.item)}catch(f){ee(f)}}return(N,f)=>(l(),h("article",{class:"book-card",style:F({width:e.width+"px"}),onClick:f[3]||(f[3]=P=>v("open",e.item)),onContextmenu:f[4]||(f[4]=ye(P=>v("menu",e.item,P),["prevent"]))},[o("div",{class:"cover",style:F({height:Math.round(e.width*1.5)+"px"})},[w.value&&!t.value?(l(),h("img",{key:0,class:ht(["cover-img",{loaded:g.value}]),src:I.value,loading:"lazy",decoding:"async",alt:"",onLoad:f[0]||(f[0]=P=>g.value=!0),onError:B},null,42,Vt)):T.value&&!x.value?(l(),h("img",{key:1,class:"cover-img cover-default",src:n(ie).defaultCover(),loading:"lazy",decoding:"async",alt:"",onError:f[1]||(f[1]=P=>x.value=!0)},null,40,qt)):(l(),h("div",{key:2,class:"cov-ph",style:F(M.value)},[o("span",Kt,y(n(oe)(e.item.format)),1),o("strong",Jt,y(e.item.title||"未命名"),1),o("small",Gt,y(e.item.author||""),1),f[5]||(f[5]=o("i",{class:"ph-spine"},null,-1))],4)),e.item.favorite?(l(),h("div",Qt,[b(n(ve),{size:13,fill:"currentColor"})])):J("",!0),e.item.missing?(l(),h("div",Zt,[b(n(yt),{size:12}),f[6]||(f[6]=L(" 文件缺失",-1))])):J("",!0),o("div",eo,[o("button",{class:"cover-btn",title:"收藏",onClick:_},[b(n(ve),{size:14,fill:e.item.favorite?"currentColor":"none"},null,8,["fill"])]),o("button",{class:"cover-btn",title:"更多",onClick:f[2]||(f[2]=ye(P=>v("menu",e.item,P),["stop"]))},[b(n(Yt),{size:15})])]),E.value>0?(l(),h("div",to,[o("i",{style:F({width:E.value+"%"})},null,4)])):J("",!0),E.value>0?(l(),h("div",oo,y(E.value)+"%",1)):J("",!0)],4),o("div",ro,[o("div",{class:"book-title",title:e.item.title},y(e.item.title||"未命名"),9,ao),o("div",so,[o("span",{class:"book-author",title:e.item.author},y(e.item.author||"未知作者"),9,no),o("span",io,y(n(oe)(e.item.format)),1)]),o("div",lo,[o("i",{style:F({width:E.value+"%"})},null,4)])])],36))}}),co={key:0,class:"detail-loading"},uo={key:1,class:"detail"},ho={class:"detail-cover-wrap"},vo={class:"detail-cover"},fo=["src"],mo={class:"ph-format"},bo={class:"ph-title"},go={class:"ph-author"},po={class:"detail-cover-side"},yo={class:"detail-badges"},wo={class:"fmt-chip"},ko={key:0,class:"warn-chip"},Co={key:1,class:"ok-chip"},$o={class:"detail-progress-row"},zo={class:"book-bar big"},So={class:"field"},xo={class:"field"},Bo={class:"detail-grid"},Eo={class:"wide"},Ro=["title"],Fo={class:"detail-path-actions"},To={class:"detail-actions"},Lo=ae({__name:"BookDetail",props:{show:{type:Boolean},item:{}},emits:["update:show","changed","deleted"],setup(e,{emit:s}){const r=e,v=s,t=$(null),x=$(!1),g=$(!1),k=$(""),w=$(""),T=$(null),I=$(0),E=p(()=>t.value?Fe(t.value.format):!1),R=$(!1),M=p(()=>!!t.value&&t.value.hasCover&&!R.value),B=p(()=>!!t.value&&!t.value.hasCover&&E.value&&re.settings.useDefaultCover&&!!re.settings.defaultCoverPath),_=p(()=>Te(t.value?.hash||"")),N=p(()=>({background:`radial-gradient(120% 100% at 20% 0%, hsl(${_.value} 42% 30%), hsl(${(_.value+46)%360} 36% 16%) 70%)`})),f=p(()=>t.value?`${ie.thumb(t.value)}&d=${I.value}`:"");async function P(){if(r.item){x.value=!0,t.value=null;try{t.value=await Z.detail(r.item.hash),R.value=!1,k.value=t.value.title,w.value=t.value.author}catch(d){ee(d),v("update:show",!1)}finally{x.value=!1}}}Ee(()=>[r.show,r.item?.hash],()=>{r.show&&P()});async function j(){if(!t.value)return;const d=k.value.trim();if(!d){K("书名不能为空","warning");return}g.value=!0;try{await Z.updateBook(t.value.hash,{title:d,author:w.value.trim()}),t.value.title=d,t.value.author=w.value.trim(),K("已保存"),v("changed")}catch(a){ee(a)}finally{g.value=!1}}async function G(){if(t.value)try{const d=!t.value.favorite;await Z.favorite(t.value.hash,d),t.value.favorite=d,K(d?"已加入收藏":"已取消收藏"),v("changed")}catch(d){ee(d)}}function X(){T.value?.click()}async function i(d){const a=d.target,H=a.files?.[0];if(a.value="",!(!H||!t.value))try{const te=await new Promise((de,ce)=>{const Q=new FileReader;Q.onload=()=>de(String(Q.result||"")),Q.onerror=()=>ce(new Error("读取图片失败")),Q.readAsDataURL(H)});await Z.updateBook(t.value.hash,{coverDataUrl:te}),t.value.hasCover=!0,R.value=!1,I.value++,K("封面已更换"),v("changed")}catch(te){ee(te)}}async function C(){if(!(!t.value||!await pt({title:"从书架移除",content:`确定移除《${t.value.title}》吗？${t.value.sourcePath?"原文件不会被删除。":"这本书是导入副本，将从程序数据目录删除。"}阅读进度与笔记也会一并移除。`,positiveText:"移除"})||!t.value))try{await Z.deleteBook(t.value.hash),v("deleted",t.value.hash),v("update:show",!1),K("已从书架移除")}catch(a){ee(a)}}function z(){t.value&&(v("update:show",!1),we.openBook(t.value.hash))}function u(){t.value&&we.reveal(t.value.filePath)}async function m(){if(t.value)try{await navigator.clipboard.writeText(t.value.filePath),K("路径已复制")}catch{K("复制失败","error")}}return(d,a)=>(l(),W(n(Lt),{show:e.show,width:440,placement:"right","onUpdate:show":a[3]||(a[3]=H=>v("update:show",H))},{default:S(()=>[b(n(Xt),{title:"书籍详情",closable:""},{default:S(()=>[x.value?(l(),h("div",co,[b(n(wt),{size:"medium"})])):t.value?(l(),h("div",uo,[o("div",ho,[o("div",vo,[M.value||B.value?(l(),h("img",{key:0,src:M.value?f.value:n(ie).defaultCover(),alt:"",onError:a[0]||(a[0]=H=>R.value=!0)},null,40,fo)):(l(),h("div",{key:1,class:"cov-ph",style:F(N.value)},[o("span",mo,y(n(oe)(t.value.format)),1),o("strong",bo,y(t.value.title),1),o("small",go,y(t.value.author),1),a[4]||(a[4]=o("i",{class:"ph-spine"},null,-1))],4))]),o("div",po,[b(n(q),{size:"small",onClick:X},{icon:S(()=>[b(n(kt))]),default:S(()=>[a[5]||(a[5]=L("更换封面",-1))]),_:1}),b(n(q),{size:"small",type:t.value.favorite?"primary":"default",onClick:G},{icon:S(()=>[b(n(ve),{size:14})]),default:S(()=>[L(y(t.value.favorite?"已收藏":"收藏"),1)]),_:1},8,["type"]),o("input",{ref_key:"coverInput",ref:T,type:"file",accept:"image/png,image/jpeg,image/webp",hidden:"",onChange:i},null,544),o("div",yo,[o("span",wo,y(n(oe)(t.value.format)),1),t.value.missing?(l(),h("span",ko,"文件缺失")):J("",!0),t.value.finished?(l(),h("span",Co,"已读")):J("",!0)])])]),o("div",$o,[a[6]||(a[6]=o("span",null,"阅读进度",-1)),o("strong",null,y(n(he)(t.value.progress))+"%",1)]),o("div",zo,[o("i",{style:F({width:n(he)(t.value.progress)+"%"})},null,4)]),o("div",So,[a[7]||(a[7]=o("label",null,"书名",-1)),b(n(Ce),{value:k.value,"onUpdate:value":a[1]||(a[1]=H=>k.value=H),placeholder:"书名"},null,8,["value"])]),o("div",xo,[a[8]||(a[8]=o("label",null,"作者",-1)),b(n(Ce),{value:w.value,"onUpdate:value":a[2]||(a[2]=H=>w.value=H),placeholder:"作者"},null,8,["value"])]),b(n(q),{block:"",loading:g.value,onClick:j},{icon:S(()=>[b(n(Ct),{size:15})]),default:S(()=>[a[9]||(a[9]=L("保存修改",-1))]),_:1},8,["loading"]),o("div",Bo,[o("div",null,[a[10]||(a[10]=o("span",null,"格式",-1)),o("b",null,y(n(oe)(t.value.format)),1)]),o("div",null,[a[11]||(a[11]=o("span",null,"大小",-1)),o("b",null,y(n(vt)(t.value.fileSize)),1)]),o("div",null,[a[12]||(a[12]=o("span",null,"笔记",-1)),o("b",null,y(t.value.notes)+" 条",1)]),o("div",null,[a[13]||(a[13]=o("span",null,"阅读时长",-1)),o("b",null,y(Math.round(t.value.readingSeconds/60))+" 分钟",1)]),o("div",null,[a[14]||(a[14]=o("span",null,"导入时间",-1)),o("b",null,y(n(ft)(t.value.addedAt)),1)]),o("div",null,[a[15]||(a[15]=o("span",null,"最近阅读",-1)),o("b",null,y(n(mt)(t.value.lastReadAt)),1)]),o("div",Eo,[a[16]||(a[16]=o("span",null,"文件位置",-1)),o("b",{class:"path",title:t.value.filePath},y(t.value.filePath),9,Ro)])]),o("div",Fo,[b(n(q),{size:"small",onClick:u},{icon:S(()=>[b(n(bt),{size:14})]),default:S(()=>[a[17]||(a[17]=L("在文件夹中显示",-1))]),_:1}),b(n(q),{size:"small",onClick:m},{icon:S(()=>[b(n($t),{size:14})]),default:S(()=>[a[18]||(a[18]=L("复制路径",-1))]),_:1})]),o("div",To,[b(n(q),{type:"primary",size:"large",block:"",disabled:t.value.missing,onClick:z},{icon:S(()=>[b(n(gt),{size:17})]),default:S(()=>[a[19]||(a[19]=L("开始阅读 ",-1))]),_:1},8,["disabled"]),b(n(q),{quaternary:"",type:"error",size:"small",onClick:C},{icon:S(()=>[b(n(zt),{size:14})]),default:S(()=>[a[20]||(a[20]=L("从书架移除 ",-1))]),_:1})])])):J("",!0)]),_:1})]),_:1},8,["show"]))}});export{No as _,Lo as a};
