import{b5 as fn,b6 as je,b7 as vn,b as ge,h as pn,b8 as gn,r as j,d as mn,b9 as hn,a as he,ba as xe,ao as r,aq as _,ap as h,bb as xn,am as O,u as De,l as u,q as H,bc as yn,e as Rt,ae as Pt,av as $t,f as oe,j as y,a7 as $,aB as It,a6 as A,bd as Cn,m as t,be as at,a9 as $e,bf as We,bg as X,bh as ot,bi as wn,bj as Sn,bk as kn,bl as zn,bm as Bt,bn as Tn,n as Ce,ac as Lt,aC as Rn,F as ae,bo as Pn,bp as $n,bq as In,D as le,br as Ge,bs as Qe,w as Be,o as At,af as Bn,aa as ft,bt as Ln,s as Le,a4 as An,a5 as _n,bu as En,bv as Wn,ai as Mn,aA as fe,ax as Ue,g as st,i as On,x as i,y as M,z as o,A as W,k as Fn,K as vt,bw as He,C as q,B as Ae,bx as Un,$ as pe,by as Hn,t as re,J as pt,a1 as Nn,bz as gt,R as ve,N as Pe,V as _e,Q as Ye,O as jn,bA as mt,U as Dn,aI as Vn,Z as Jn,bB as qn,bC as Xn}from"./index-Bw53uBzP.js";import{c as Kn,a as ht,o as Gn,S as ce}from"./Select-BvAgo-sr.js";import{A as Qn,I as Ne}from"./InputNumber-bfotet15.js";import{D as Yn,P as Zn}from"./database-Bhm69Or_.js";import{S as ye,R as ea,V as ta}from"./volume-2-tVABjnRz.js";import{S as ne}from"./Switch-XI7JM4N0.js";import{u as na}from"./Input-WzzQ6Td5.js";import{u as xt}from"./use-compitable-Bx93lIHo.js";import{_ as aa}from"./PageHeader.vue_vue_type_script_setup_true_lang-CpFh_cKw.js";import{I as Ze}from"./image-plus-ClKSm-8p.js";import{T as la}from"./type-Cu5W2PgS.js";import{S as oa}from"./save-DZgNxIfI.js";import{R as sa}from"./refresh-cw-Dw-E_trt.js";import{I as ra}from"./info-FddPpqLj.js";import"./format-length-B-p6aW7q.js";var ia=/\s/;function da(n){for(var d=n.length;d--&&ia.test(n.charAt(d)););return d}var ua=/^\s+/;function ca(n){return n&&n.slice(0,da(n)+1).replace(ua,"")}var yt=NaN,ba=/^[-+]0x[0-9a-f]+$/i,fa=/^0b[01]+$/i,va=/^0o[0-7]+$/i,pa=parseInt;function Ct(n){if(typeof n=="number")return n;if(fn(n))return yt;if(je(n)){var d=typeof n.valueOf=="function"?n.valueOf():n;n=je(d)?d+"":d}if(typeof n!="string")return n===0?n:+n;n=ca(n);var c=fa.test(n);return c||va.test(n)?pa(n.slice(2),c?2:8):ba.test(n)?yt:+n}var et=function(){return vn.Date.now()},ga="Expected a function",ma=Math.max,ha=Math.min;function xa(n,d,c){var w,p,C,R,x,S,k=0,g=!1,m=!1,v=!0;if(typeof n!="function")throw new TypeError(ga);d=Ct(d)||0,je(c)&&(g=!!c.leading,m="maxWait"in c,C=m?ma(Ct(c.maxWait)||0,d):C,v="trailing"in c?!!c.trailing:v);function B(I){var L=w,Q=p;return w=p=void 0,k=I,R=n.apply(Q,L),R}function T(I){return k=I,x=setTimeout(F,d),g?B(I):R}function E(I){var L=I-S,Q=I-k,K=d-L;return m?ha(K,C-Q):K}function V(I){var L=I-S,Q=I-k;return S===void 0||L>=d||L<0||m&&Q>=C}function F(){var I=et();if(V(I))return Z(I);x=setTimeout(F,E(I))}function Z(I){return x=void 0,v&&w?B(I):(w=p=void 0,R)}function ie(){x!==void 0&&clearTimeout(x),k=0,w=S=p=x=void 0}function Y(){return x===void 0?R:Z(et())}function ee(){var I=et(),L=V(I);if(w=arguments,p=this,S=I,L){if(x===void 0)return T(S);if(m)return clearTimeout(x),x=setTimeout(F,d),B(S)}return x===void 0&&(x=setTimeout(F,d)),R}return ee.cancel=ie,ee.flush=Y,ee}var ya="Expected a function";function Ca(n,d,c){var w=!0,p=!0;if(typeof n!="function")throw new TypeError(ya);return je(c)&&(w="leading"in c?!!c.leading:w,p="trailing"in c?!!c.trailing:p),xa(n,d,{leading:w,maxWait:d,trailing:p})}const wa=ht(".v-x-scroll",{overflow:"auto",scrollbarWidth:"none"},[ht("&::-webkit-scrollbar",{width:0,height:0})]),Sa=ge({name:"XScroll",props:{disabled:Boolean,onScroll:Function},setup(){const n=j(null);function d(p){!(p.currentTarget.offsetWidth<p.currentTarget.scrollWidth)||p.deltaY===0||(p.currentTarget.scrollLeft+=p.deltaY+p.deltaX,p.preventDefault())}const c=gn();return wa.mount({id:"vueuc/x-scroll",head:!0,anchorMetaName:Kn,ssr:c}),Object.assign({selfRef:n,handleWheel:d},{scrollTo(...p){var C;(C=n.value)===null||C===void 0||C.scrollTo(...p)}})},render(){return pn("div",{ref:"selfRef",onScroll:this.onScroll,onWheel:this.disabled?void 0:this.handleWheel,class:"v-x-scroll"},this.$slots)}});function ka(n){const{lineHeight:d,borderRadius:c,fontWeightStrong:w,baseColor:p,dividerColor:C,actionColor:R,textColor1:x,textColor2:S,closeColorHover:k,closeColorPressed:g,closeIconColor:m,closeIconColorHover:v,closeIconColorPressed:B,infoColor:T,successColor:E,warningColor:V,errorColor:F,fontSize:Z}=n;return{...hn,fontSize:Z,lineHeight:d,titleFontWeight:w,borderRadius:c,border:`1px solid ${C}`,color:R,titleTextColor:x,iconColor:S,contentTextColor:S,closeBorderRadius:c,closeColorHover:k,closeColorPressed:g,closeIconColor:m,closeIconColorHover:v,closeIconColorPressed:B,borderInfo:`1px solid ${he(p,xe(T,{alpha:.25}))}`,colorInfo:he(p,xe(T,{alpha:.08})),titleTextColorInfo:x,iconColorInfo:T,contentTextColorInfo:S,closeColorHoverInfo:k,closeColorPressedInfo:g,closeIconColorInfo:m,closeIconColorHoverInfo:v,closeIconColorPressedInfo:B,borderSuccess:`1px solid ${he(p,xe(E,{alpha:.25}))}`,colorSuccess:he(p,xe(E,{alpha:.08})),titleTextColorSuccess:x,iconColorSuccess:E,contentTextColorSuccess:S,closeColorHoverSuccess:k,closeColorPressedSuccess:g,closeIconColorSuccess:m,closeIconColorHoverSuccess:v,closeIconColorPressedSuccess:B,borderWarning:`1px solid ${he(p,xe(V,{alpha:.33}))}`,colorWarning:he(p,xe(V,{alpha:.08})),titleTextColorWarning:x,iconColorWarning:V,contentTextColorWarning:S,closeColorHoverWarning:k,closeColorPressedWarning:g,closeIconColorWarning:m,closeIconColorHoverWarning:v,closeIconColorPressedWarning:B,borderError:`1px solid ${he(p,xe(F,{alpha:.25}))}`,colorError:he(p,xe(F,{alpha:.08})),titleTextColorError:x,iconColorError:F,contentTextColorError:S,closeColorHoverError:k,closeColorPressedError:g,closeIconColorError:m,closeIconColorHoverError:v,closeIconColorPressedError:B}}const za={common:mn,self:ka};var Ta=r("alert",`
 line-height: var(--n-line-height);
 border-radius: var(--n-border-radius);
 position: relative;
 transition: background-color .3s var(--n-bezier);
 background-color: var(--n-color);
 text-align: start;
 word-break: break-word;
`,[_("border",`
 border-radius: inherit;
 position: absolute;
 left: 0;
 right: 0;
 top: 0;
 bottom: 0;
 transition: border-color .3s var(--n-bezier);
 border: var(--n-border);
 pointer-events: none;
 `),h("closable",[r("alert-body",[_("title",`
 padding-right: 24px;
 `)])]),_("icon",{color:"var(--n-icon-color)"}),r("alert-body",{padding:"var(--n-padding)"},[_("title",{color:"var(--n-title-text-color)"}),_("content",{color:"var(--n-content-text-color)"})]),xn({originalTransition:"transform .3s var(--n-bezier)",enterToProps:{transform:"scale(1)"},leaveToProps:{transform:"scale(0.9)"}}),_("icon",`
 position: absolute;
 left: 0;
 top: 0;
 align-items: center;
 justify-content: center;
 display: flex;
 width: var(--n-icon-size);
 height: var(--n-icon-size);
 font-size: var(--n-icon-size);
 margin: var(--n-icon-margin);
 `),_("close",`
 transition:
 color .3s var(--n-bezier),
 background-color .3s var(--n-bezier);
 position: absolute;
 right: 0;
 top: 0;
 margin: var(--n-close-margin);
 `),h("show-icon",[r("alert-body",{paddingLeft:"calc(var(--n-icon-margin-left) + var(--n-icon-size) + var(--n-icon-margin-right))"})]),h("right-adjust",[r("alert-body",{paddingRight:"calc(var(--n-close-size) + var(--n-padding) + 2px)"})]),r("alert-body",`
 border-radius: var(--n-border-radius);
 transition: border-color .3s var(--n-bezier);
 `,[_("title",`
 transition: color .3s var(--n-bezier);
 font-size: 16px;
 line-height: 19px;
 font-weight: var(--n-title-font-weight);
 `,[O("& +",[_("content",{marginTop:"9px"})])]),_("content",{transition:"color .3s var(--n-bezier)",fontSize:"var(--n-font-size)"})]),_("icon",{transition:"color .3s var(--n-bezier)"})]);const Ra={...De.props,title:String,showIcon:{type:Boolean,default:!0},type:{type:String,default:"default"},bordered:{type:Boolean,default:!0},closable:Boolean,onClose:Function,onAfterLeave:Function,onAfterHide:Function};var Pa=ge({name:"Alert",inheritAttrs:!1,props:Ra,slots:Object,setup(n){const{mergedClsPrefixRef:d,mergedBorderedRef:c,inlineThemeDisabled:w,mergedRtlRef:p}=Rt(n),C=De("Alert","-alert",Ta,za,n,d),R=Pt("Alert",p,d),x=oe(()=>{const{common:{cubicBezierEaseInOut:B},self:T}=C.value,{fontSize:E,borderRadius:V,titleFontWeight:F,lineHeight:Z,iconSize:ie,iconMargin:Y,iconMarginRtl:ee,closeIconSize:I,closeBorderRadius:L,closeSize:Q,closeMargin:K,closeMarginRtl:me,padding:te}=T,{type:P}=n,{left:D,right:de}=We(Y);return{"--n-bezier":B,"--n-color":T[X("color",P)],"--n-close-icon-size":I,"--n-close-border-radius":L,"--n-close-color-hover":T[X("closeColorHover",P)],"--n-close-color-pressed":T[X("closeColorPressed",P)],"--n-close-icon-color":T[X("closeIconColor",P)],"--n-close-icon-color-hover":T[X("closeIconColorHover",P)],"--n-close-icon-color-pressed":T[X("closeIconColorPressed",P)],"--n-icon-color":T[X("iconColor",P)],"--n-border":T[X("border",P)],"--n-title-text-color":T[X("titleTextColor",P)],"--n-content-text-color":T[X("contentTextColor",P)],"--n-line-height":Z,"--n-border-radius":V,"--n-font-size":E,"--n-title-font-weight":F,"--n-icon-size":ie,"--n-icon-margin":Y,"--n-icon-margin-rtl":ee,"--n-close-size":Q,"--n-close-margin":K,"--n-close-margin-rtl":me,"--n-padding":te,"--n-icon-margin-left":D,"--n-icon-margin-right":de}}),S=w?$t("alert",oe(()=>n.type[0]),x,n):void 0,k=j(!0),g=()=>{const{onAfterLeave:B,onAfterHide:T}=n;B&&B(),T&&T()};return{rtlEnabled:R,mergedClsPrefix:d,mergedBordered:c,visible:k,handleCloseClick:()=>{Promise.resolve(n.onClose?.()).then(B=>{B!==!1&&(k.value=!1)})},handleAfterLeave:()=>{g()},mergedTheme:C,cssVars:w?void 0:x,themeClass:S?.themeClass,onRender:S?.onRender}},render(){return this.onRender?.(),u(),H(yn,{onAfterLeave:this.handleAfterLeave},{default:()=>{const{mergedClsPrefix:n,$slots:d}=this,c={class:[`${n}-alert`,this.themeClass,this.closable&&`${n}-alert--closable`,this.showIcon&&`${n}-alert--show-icon`,!this.title&&this.closable&&`${n}-alert--right-adjust`,this.rtlEnabled&&`${n}-alert--rtl`],style:this.cssVars,role:"alert"};return this.visible?(u(),y("div",$e({key:1},$e(this.$attrs,c)),[$(()=>this.closable&&(u(),H(It,{clsPrefix:n,class:A(`${n}-alert__close`),onClick:this.handleCloseClick},null,8,["clsPrefix","class","onClick"]))),$(()=>this.bordered&&(u(),y("div",{class:A(`${n}-alert__border`)},null,2))),$(()=>this.showIcon&&(u(),y("div",{class:A(`${n}-alert__icon`),"aria-hidden":"true"},[$(()=>Cn(d.icon,()=>[(u(),H(ot,{clsPrefix:n},{default:()=>{switch(this.type){case"success":return u(),H(zn,{key:3});case"info":return u(),H(kn,{key:4});case"warning":return u(),H(Sn,{key:5});case"error":return u(),H(wn,{key:6});default:return null}}},1032,["clsPrefix"]))]))],2))),t("div",{class:A([`${n}-alert-body`,this.mergedBordered&&`${n}-alert-body--bordered`])},[$(()=>at(d.header,w=>{const p=w||this.title;return p?(u(),y("div",{key:2,class:A(`${n}-alert-body__title`)},[$(()=>p)],2)):null})),$(()=>d.default&&(u(),y("div",{class:A(`${n}-alert-body__content`)},[$(()=>d.default())],2)))],2)],16)):null}},1032,["onAfterLeave"])}}),$a=ge({name:"ChevronLeft",render(){return(()=>{const n=Bt("dfe229c2639b2082");return n[0]||(n[0]=t("svg",{viewBox:"0 0 16 16",fill:"none",xmlns:"http://www.w3.org/2000/svg"},[t("path",{d:"M10.3536 3.14645C10.5488 3.34171 10.5488 3.65829 10.3536 3.85355L6.20711 8L10.3536 12.1464C10.5488 12.3417 10.5488 12.6583 10.3536 12.8536C10.1583 13.0488 9.84171 13.0488 9.64645 12.8536L5.14645 8.35355C4.95118 8.15829 4.95118 7.84171 5.14645 7.64645L9.64645 3.14645C9.84171 2.95118 10.1583 2.95118 10.3536 3.14645Z",fill:"currentColor"})],-1))})()}}),Ia=ge({name:"ChevronRight",render(){return(()=>{const n=Bt("6ab04425f4fcb756");return n[0]||(n[0]=t("svg",{viewBox:"0 0 16 16",fill:"none",xmlns:"http://www.w3.org/2000/svg"},[t("path",{d:"M5.64645 3.14645C5.45118 3.34171 5.45118 3.65829 5.64645 3.85355L9.79289 8L5.64645 12.1464C5.45118 12.3417 5.45118 12.6583 5.64645 12.8536C5.84171 13.0488 6.15829 13.0488 6.35355 12.8536L10.8536 8.35355C11.0488 8.15829 11.0488 7.84171 10.8536 7.64645L6.35355 3.14645C6.15829 2.95118 5.84171 2.95118 5.64645 3.14645Z",fill:"currentColor"})],-1))})()}});const rt=Tn("n-tabs"),_t={tab:[String,Number,Object,Function],name:{type:[String,Number],required:!0},disabled:Boolean,displayDirective:{type:String,default:"if"},closable:{type:Boolean,default:void 0},tabProps:Object,label:[String,Number,Object,Function]};var Ee=ge({__TAB_PANE__:!0,name:"TabPane",alias:["TabPanel"],props:_t,slots:Object,setup(n){const d=Lt(rt,null);return d||Rn("tab-pane","`n-tab-pane` must be placed inside `n-tabs`."),{style:d.paneStyleRef,class:d.paneClassRef,mergedClsPrefix:d.mergedClsPrefixRef}},render(){return u(),y("div",{class:A([`${this.mergedClsPrefix}-tab-pane`,this.class]),style:Ce(this.style)},[$(()=>this.$slots.default?.())],6)}});const Ba=["data-name","data-disabled"],La={internalLeftPadded:Boolean,internalAddable:Boolean,internalCreatedByPane:Boolean,...$n(_t,["displayDirective"])};var lt=ge({__TAB__:!0,inheritAttrs:!1,name:"Tab",props:La,setup(n){const{mergedClsPrefixRef:d,valueRef:c,typeRef:w,closableRef:p,tabStyleRef:C,addTabStyleRef:R,tabClassRef:x,addTabClassRef:S,tabChangeIdRef:k,onBeforeLeaveRef:g,triggerRef:m,handleAdd:v,activateTab:B,handleClose:T}=Lt(rt);return{trigger:m,mergedClosable:oe(()=>{if(n.internalAddable)return!1;const{closable:E}=n;return E===void 0?p.value:E}),style:C,addStyle:R,tabClass:x,addTabClass:S,clsPrefix:d,value:c,type:w,handleClose(E){E.stopPropagation(),!n.disabled&&T(n.name)},activateTab(){if(n.disabled)return;if(n.internalAddable){v();return}const{name:E}=n,V=++k.id;if(E!==c.value){const{value:F}=g;F?Promise.resolve(F(n.name,c.value)).then(Z=>{Z&&k.id===V&&B(E)}):B(E)}}}},render(){const{internalAddable:n,clsPrefix:d,name:c,disabled:w,label:p,tab:C,value:R,mergedClosable:x,trigger:S,$slots:{default:k}}=this,g=p??C;return u(),y("div",{class:A(`${d}-tabs-tab-wrapper`)},[this.internalLeftPadded?(u(),y("div",{key:0,class:A(`${d}-tabs-tab-pad`)},null,2)):$(()=>null),(u(),y("div",$e({key:c,"data-name":c,"data-disabled":w?!0:void 0},$e({class:[`${d}-tabs-tab`,R===c&&`${d}-tabs-tab--active`,w&&`${d}-tabs-tab--disabled`,x&&`${d}-tabs-tab--closable`,n&&`${d}-tabs-tab--addable`,n?this.addTabClass:this.tabClass],onClick:S==="click"?this.activateTab:void 0,onMouseenter:S==="hover"?this.activateTab:void 0,style:n?this.addStyle:this.style},this.internalCreatedByPane?this.tabProps||{}:this.$attrs)),[t("span",{class:A(`${d}-tabs-tab__label`)},[n?(u(),y(ae,{key:0},[t("div",{class:A(`${d}-tabs-tab__height-placeholder`)}," ",2),(u(),H(ot,{clsPrefix:d},{default:()=>(u(),H(Qn))},1032,["clsPrefix"]))],64)):(u(),y(ae,{key:1},[k?(u(),y(ae,{key:0},[$(()=>k())],64)):(u(),y(ae,{key:1},[typeof g=="object"?(u(),y(ae,{key:0},[$(()=>g)],64)):(u(),y(ae,{key:1},[$(()=>Pn(g??c))],64))],64))],64))],2),x&&this.type==="card"?(u(),H(It,{key:0,clsPrefix:d,class:A(`${d}-tabs-tab__close`),onClick:this.handleClose,disabled:w},null,8,["clsPrefix","class","onClick","disabled"])):$(()=>null)],16,Ba))],2)}}),Aa=r("tabs",`
 box-sizing: border-box;
 width: 100%;
 display: flex;
 flex-direction: column;
 transition:
 background-color .3s var(--n-bezier),
 border-color .3s var(--n-bezier);
`,[O("&.transition-disabled",[r("tabs-tab",`
 transition: none !important;
 `),r("tabs-nav-scroll-content",`
 transition: none !important;
 `),r("tabs-tab-pad",`
 transition: none !important;
 `)]),h("segment-type",[r("tabs-rail",[O("&.transition-disabled",[r("tabs-capsule",`
 transition: none;
 `)])])]),h("top",[r("tab-pane",`
 padding: var(--n-pane-padding-top) var(--n-pane-padding-right) var(--n-pane-padding-bottom) var(--n-pane-padding-left);
 `)]),h("left",[r("tab-pane",`
 padding: var(--n-pane-padding-right) var(--n-pane-padding-bottom) var(--n-pane-padding-left) var(--n-pane-padding-top);
 `)]),h("left, right",`
 flex-direction: row;
 `,[r("tabs-bar",`
 width: 2px;
 right: 0;
 transition:
 top .2s var(--n-bezier),
 max-height .2s var(--n-bezier),
 background-color .3s var(--n-bezier);
 `),r("tabs-tab",`
 padding: var(--n-tab-padding-vertical); 
 `)]),h("right",`
 flex-direction: row-reverse;
 `,[r("tab-pane",`
 padding: var(--n-pane-padding-left) var(--n-pane-padding-top) var(--n-pane-padding-right) var(--n-pane-padding-bottom);
 `),r("tabs-bar",`
 left: 0;
 `)]),h("bottom",`
 flex-direction: column-reverse;
 justify-content: flex-end;
 `,[r("tab-pane",`
 padding: var(--n-pane-padding-bottom) var(--n-pane-padding-right) var(--n-pane-padding-top) var(--n-pane-padding-left);
 `),r("tabs-bar",`
 top: 0;
 `)]),r("tabs-rail",`
 position: relative;
 padding: 3px;
 border-radius: var(--n-tab-border-radius);
 width: 100%;
 background-color: var(--n-color-segment);
 transition: background-color .3s var(--n-bezier);
 display: flex;
 align-items: center;
 `,[r("tabs-capsule",`
 border-radius: var(--n-tab-border-radius);
 position: absolute;
 left: 0;
 top: 0;
 pointer-events: none;
 background-color: var(--n-tab-color-segment);
 box-shadow: 0 1px 3px 0 rgba(0, 0, 0, .08);
 transition: transform 0.3s var(--n-bezier);
 `),r("tabs-tab-wrapper",`
 flex-basis: 0;
 flex-grow: 1;
 display: flex;
 align-items: center;
 justify-content: center;
 `,[r("tabs-tab",`
 overflow: hidden;
 border-radius: var(--n-tab-border-radius);
 width: 100%;
 display: flex;
 align-items: center;
 justify-content: center;
 `,[h("active",`
 font-weight: var(--n-font-weight-strong);
 color: var(--n-tab-text-color-active);
 `),O("&:hover",`
 color: var(--n-tab-text-color-hover);
 `)])])]),h("flex",[r("tabs-nav",`
 width: 100%;
 position: relative;
 `,[r("tabs-wrapper",`
 width: 100%;
 `,[r("tabs-tab",`
 margin-right: 0;
 `)])])]),r("tabs-nav",`
 box-sizing: border-box;
 line-height: 1.5;
 display: flex;
 transition: border-color .3s var(--n-bezier);
 `,[_("prefix, suffix",`
 display: flex;
 align-items: center;
 `),_("prefix","padding-right: 16px;"),_("suffix","padding-left: 16px;")]),h("top, bottom",[O(">",[r("tabs-nav",[r("tabs-nav-scroll-wrapper",[O("&::before",`
 top: 0;
 bottom: 0;
 left: 0;
 width: 20px;
 `),O("&::after",`
 top: 0;
 bottom: 0;
 right: 0;
 width: 20px;
 `),h("shadow-start",[O("&::before",`
 box-shadow: inset 10px 0 8px -8px rgba(0, 0, 0, .12);
 `)]),h("shadow-end",[O("&::after",`
 box-shadow: inset -10px 0 8px -8px rgba(0, 0, 0, .12);
 `)])])])])]),h("left, right",[r("tabs-nav-scroll-content",`
 flex-direction: column;
 `),O(">",[r("tabs-nav",[r("tabs-nav-scroll-wrapper",[O("&::before",`
 top: 0;
 left: 0;
 right: 0;
 height: 20px;
 `),O("&::after",`
 bottom: 0;
 left: 0;
 right: 0;
 height: 20px;
 `),h("shadow-start",[O("&::before",`
 box-shadow: inset 0 10px 8px -8px rgba(0, 0, 0, .12);
 `)]),h("shadow-end",[O("&::after",`
 box-shadow: inset 0 -10px 8px -8px rgba(0, 0, 0, .12);
 `)])])])])]),r("tabs-nav-scroll-wrapper",`
 flex: 1;
 position: relative;
 overflow: hidden;
 `,[r("tabs-nav-y-scroll",`
 height: 100%;
 width: 100%;
 overflow-y: auto; 
 scrollbar-width: none;
 `,[O("&::-webkit-scrollbar, &::-webkit-scrollbar-track-piece, &::-webkit-scrollbar-thumb",`
 width: 0;
 height: 0;
 display: none;
 `)]),O("&::before, &::after",`
 transition: box-shadow .3s var(--n-bezier);
 pointer-events: none;
 content: "";
 position: absolute;
 z-index: 1;
 `),O("&.transition-disabled",[O("&::before, &::after",`
 transition: none;
 `)])]),r("tabs-nav-scroll-content",`
 display: flex;
 position: relative;
 min-width: 100%;
 min-height: 100%;
 width: fit-content;
 box-sizing: border-box;
 `),r("tabs-wrapper",`
 display: inline-flex;
 flex-wrap: nowrap;
 position: relative;
 `),r("tabs-tab-wrapper",`
 display: flex;
 flex-wrap: nowrap;
 flex-shrink: 0;
 flex-grow: 0;
 `),r("tabs-tab",`
 cursor: pointer;
 white-space: nowrap;
 flex-wrap: nowrap;
 display: inline-flex;
 align-items: center;
 color: var(--n-tab-text-color);
 font-size: var(--n-tab-font-size);
 background-clip: padding-box;
 padding: var(--n-tab-padding);
 transition:
 box-shadow .3s var(--n-bezier),
 color .3s var(--n-bezier),
 background-color .3s var(--n-bezier),
 border-color .3s var(--n-bezier);
 `,[h("disabled",{cursor:"not-allowed"}),_("close",`
 margin-inline-start: 6px;
 transition:
 background-color .3s var(--n-bezier),
 color .3s var(--n-bezier);
 `),_("label",`
 display: flex;
 align-items: center;
 z-index: 1;
 `)]),r("tabs-bar",`
 position: absolute;
 bottom: 0;
 height: 2px;
 border-radius: 1px;
 background-color: var(--n-bar-color);
 transition:
 left .2s var(--n-bezier),
 max-width .2s var(--n-bezier),
 opacity .3s var(--n-bezier),
 background-color .3s var(--n-bezier);
 `,[O("&.transition-disabled",`
 transition: none;
 `),h("disabled",`
 background-color: var(--n-tab-text-color-disabled)
 `)]),r("tabs-pane-wrapper",`
 position: relative;
 overflow: hidden;
 transition: max-height .2s var(--n-bezier);
 `),r("tab-pane",`
 color: var(--n-pane-text-color);
 width: 100%;
 transition:
 color .3s var(--n-bezier),
 background-color .3s var(--n-bezier),
 opacity .2s var(--n-bezier);
 left: 0;
 right: 0;
 top: 0;
 `,[O("&.next-transition-leave-active, &.prev-transition-leave-active, &.next-transition-enter-active, &.prev-transition-enter-active",`
 transition:
 color .3s var(--n-bezier),
 background-color .3s var(--n-bezier),
 transform .2s var(--n-bezier),
 opacity .2s var(--n-bezier);
 `),O("&.next-transition-leave-active, &.prev-transition-leave-active",`
 position: absolute;
 `),O("&.next-transition-enter-from, &.prev-transition-leave-to",`
 transform: translateX(32px);
 opacity: 0;
 `),O("&.next-transition-leave-to, &.prev-transition-enter-from",`
 transform: translateX(-32px);
 opacity: 0;
 `),O("&.next-transition-leave-from, &.next-transition-enter-to, &.prev-transition-leave-from, &.prev-transition-enter-to",`
 transform: translateX(0);
 opacity: 1;
 `)]),r("tabs-tab-pad",`
 box-sizing: border-box;
 width: var(--n-tab-gap);
 flex-grow: 0;
 flex-shrink: 0;
 `),h("line-type, bar-type",[r("tabs-tab",`
 font-weight: var(--n-tab-font-weight);
 box-sizing: border-box;
 vertical-align: bottom;
 `,[O("&:hover",{color:"var(--n-tab-text-color-hover)"}),h("active",`
 color: var(--n-tab-text-color-active);
 font-weight: var(--n-tab-font-weight-active);
 `),h("disabled",{color:"var(--n-tab-text-color-disabled)"})])]),r("tabs-nav",[_("prefix, suffix",`
 border-color: var(--n-tab-border-color);
 `),r("tabs-nav-scroll-content",`
 border-color: var(--n-tab-border-color);
 `),h("line-type",[h("top",[_("prefix, suffix",`
 border-bottom: 1px solid var(--n-tab-border-color);
 `),r("tabs-nav-scroll-content",`
 border-bottom: 1px solid var(--n-tab-border-color);
 `),r("tabs-bar",`
 bottom: -1px;
 `)]),h("left",[_("prefix, suffix",`
 border-right: 1px solid var(--n-tab-border-color);
 `),r("tabs-nav-scroll-content",`
 border-right: 1px solid var(--n-tab-border-color);
 `),r("tabs-bar",`
 right: -1px;
 `)]),h("right",[_("prefix, suffix",`
 border-left: 1px solid var(--n-tab-border-color);
 `),r("tabs-nav-scroll-content",`
 border-left: 1px solid var(--n-tab-border-color);
 `),r("tabs-bar",`
 left: -1px;
 `)]),h("bottom",[_("prefix, suffix",`
 border-top: 1px solid var(--n-tab-border-color);
 `),r("tabs-nav-scroll-content",`
 border-top: 1px solid var(--n-tab-border-color);
 `),r("tabs-bar",`
 top: -1px;
 `)]),_("prefix, suffix",`
 transition: border-color .3s var(--n-bezier);
 `),r("tabs-nav-scroll-content",`
 transition: border-color .3s var(--n-bezier);
 `),r("tabs-bar",`
 border-radius: 0;
 `)]),h("card-type",[_("prefix, suffix",`
 transition: border-color .3s var(--n-bezier);
 `),r("tabs-pad",`
 flex-grow: 1;
 transition: border-color .3s var(--n-bezier);
 `),r("tabs-tab-pad",`
 transition: border-color .3s var(--n-bezier);
 `),r("tabs-tab",`
 font-weight: var(--n-tab-font-weight);
 border: 1px solid var(--n-tab-border-color);
 background-color: var(--n-tab-color);
 box-sizing: border-box;
 position: relative;
 vertical-align: bottom;
 display: flex;
 justify-content: space-between;
 font-size: var(--n-tab-font-size);
 color: var(--n-tab-text-color);
 `,[h("addable",`
 padding-left: 8px;
 padding-right: 8px;
 font-size: 16px;
 justify-content: center;
 `,[_("height-placeholder",`
 width: 0;
 font-size: var(--n-tab-font-size);
 `),In("disabled",[O("&:hover",`
 color: var(--n-tab-text-color-hover);
 `)])]),h("closable","padding-inline-end: 8px;"),h("active",`
 background-color: #0000;
 font-weight: var(--n-tab-font-weight-active);
 color: var(--n-tab-text-color-active);
 `),h("disabled","color: var(--n-tab-text-color-disabled);")])]),h("left, right",`
 flex-direction: column; 
 `,[_("prefix, suffix",`
 padding: var(--n-tab-padding-vertical);
 `),r("tabs-wrapper",`
 flex-direction: column;
 `),r("tabs-tab-wrapper",`
 flex-direction: column;
 `,[r("tabs-tab-pad",`
 height: var(--n-tab-gap-vertical);
 width: 100%;
 `)])]),h("top",[h("card-type",[r("tabs-scroll-padding","border-bottom: 1px solid var(--n-tab-border-color);"),_("prefix, suffix",`
 border-bottom: 1px solid var(--n-tab-border-color);
 `),r("tabs-tab",`
 border-top-left-radius: var(--n-tab-border-radius);
 border-top-right-radius: var(--n-tab-border-radius);
 `,[h("active",`
 border-bottom: 1px solid #0000;
 `)]),r("tabs-tab-pad",`
 border-bottom: 1px solid var(--n-tab-border-color);
 `),r("tabs-pad",`
 border-bottom: 1px solid var(--n-tab-border-color);
 `)])]),h("left",[h("card-type",[r("tabs-scroll-padding","border-right: 1px solid var(--n-tab-border-color);"),_("prefix, suffix",`
 border-right: 1px solid var(--n-tab-border-color);
 `),r("tabs-tab",`
 border-top-left-radius: var(--n-tab-border-radius);
 border-bottom-left-radius: var(--n-tab-border-radius);
 `,[h("active",`
 border-right: 1px solid #0000;
 `)]),r("tabs-tab-pad",`
 border-right: 1px solid var(--n-tab-border-color);
 `),r("tabs-pad",`
 border-right: 1px solid var(--n-tab-border-color);
 `)])]),h("right",[h("card-type",[r("tabs-scroll-padding","border-left: 1px solid var(--n-tab-border-color);"),_("prefix, suffix",`
 border-left: 1px solid var(--n-tab-border-color);
 `),r("tabs-tab",`
 border-top-right-radius: var(--n-tab-border-radius);
 border-bottom-right-radius: var(--n-tab-border-radius);
 `,[h("active",`
 border-left: 1px solid #0000;
 `)]),r("tabs-tab-pad",`
 border-left: 1px solid var(--n-tab-border-color);
 `),r("tabs-pad",`
 border-left: 1px solid var(--n-tab-border-color);
 `)])]),h("bottom",[h("card-type",[r("tabs-scroll-padding","border-top: 1px solid var(--n-tab-border-color);"),_("prefix, suffix",`
 border-top: 1px solid var(--n-tab-border-color);
 `),r("tabs-tab",`
 border-bottom-left-radius: var(--n-tab-border-radius);
 border-bottom-right-radius: var(--n-tab-border-radius);
 `,[h("active",`
 border-top: 1px solid #0000;
 `)]),r("tabs-tab-pad",`
 border-top: 1px solid var(--n-tab-border-color);
 `),r("tabs-pad",`
 border-top: 1px solid var(--n-tab-border-color);
 `)])])]),r("tabs-scroll-button",[h("start",`
 padding-left: 10px;
 padding-right: 6px;
 `),h("end",`
 padding-right: 10px;
 padding-left: 6px;
 `),h("up",`
 padding-bottom: 10px;
 `),h("down",`
 padding-top: 10px;
 `)])]),wt=ge({name:"TabsButton",props:{type:{type:String,default:"next"},mergedClsPrefix:{type:String,required:!0},vertical:Boolean,disabled:Boolean,rtl:Boolean,theme:Object,themeOverrides:Object,onClick:Function},setup(n){return{handleClick:()=>{n.disabled||n.onClick?.(n.type)}}},render(){const{mergedClsPrefix:n,disabled:d,type:c,vertical:w,rtl:p,theme:C,themeOverrides:R,handleClick:x}=this,S=c==="next",k=w?S:p?!S:S;return u(),H(le,{text:!0,disabled:d,size:"small",theme:C,themeOverrides:R,onClick:x,class:A([`${n}-tabs-scroll-button`,!w&&c==="prev"&&`${n}-tabs-scroll-button--start`,!w&&c==="next"&&`${n}-tabs-scroll-button--end`,w&&c==="prev"&&`${n}-tabs-scroll-button--up`,w&&c==="next"&&`${n}-tabs-scroll-button--down`])},{icon:()=>(u(),H(ot,{clsPrefix:n,style:Ce(w?{transform:"rotate(90deg)"}:void 0)},{default:()=>k?(u(),H(Ia,{key:1})):(u(),H($a,{key:2}))},1032,["clsPrefix","style"]))},1032,["disabled","theme","themeOverrides","onClick","class"])}});const tt=Ca,_a={...De.props,value:[String,Number],defaultValue:[String,Number],trigger:{type:String,default:"click"},type:{type:String,default:"bar"},closable:Boolean,justifyContent:String,size:String,placement:{type:String,default:"top"},tabStyle:[String,Object],tabClass:String,addTabStyle:[String,Object],addTabClass:String,barWidth:Number,paneClass:String,paneStyle:[String,Object],paneWrapperClass:String,paneWrapperStyle:[String,Object],addable:[Boolean,Object],tabsPadding:{type:Number,default:0},animated:Boolean,onBeforeLeave:Function,onAdd:Function,"onUpdate:value":[Function,Array],onUpdateValue:[Function,Array],onClose:[Function,Array],labelSize:String,activeName:[String,Number],onActiveNameChange:[Function,Array],showScrollButton:Boolean,centerActiveTab:Boolean};var Ea=ge({name:"Tabs",props:_a,slots:Object,setup(n,{slots:d}){const{mergedClsPrefixRef:c,inlineThemeDisabled:w,mergedComponentPropsRef:p,mergedRtlRef:C}=Rt(n),R=Pt("Tabs",C,c),x=oe(()=>{const{placement:a}=n;return a==="start"?R?.value?"right":"left":a==="end"?R?.value?"left":"right":a}),S=De("Tabs","-tabs",Aa,Ln,n,c),k=j(null),g=j(null),m=j(null),v=j(null),B=j(null),T=j(null),E=j(null),V=j(!0),F=j(!0),Z=xt(n,["labelSize","size"]),ie=oe(()=>{if(Z.value)return Z.value;const a=p?.value?.Tabs?.size;return a||"medium"}),Y=xt(n,["activeName","value"]),ee=j(Y.value??n.defaultValue??(d.default?Ge(d.default())[0]?.props?.name:null)),I=na(Y,ee),L={id:0},Q=oe(()=>{if(!(!n.justifyContent||n.type==="card"))return{display:"flex",justifyContent:n.justifyContent}});Be(I,()=>{L.id=0,D(),Le(()=>{Ie()})});function K(){const{value:a}=I;return a===null?null:k.value?.querySelector(`[data-name="${a}"]`)}function me(a){if(n.type==="card")return;const{value:s}=m;if(!s)return;const f=s.style.opacity==="0";if(a){const z=`${c.value}-tabs-bar--disabled`,{barWidth:U}=n,J=x.value;if(a.dataset.disabled==="true"?s.classList.add(z):s.classList.remove(z),["top","bottom"].includes(J)){if(P(["top","maxHeight","height"]),typeof U=="number"&&a.offsetWidth>=U){const N=Math.floor((a.offsetWidth-U)/2)+a.offsetLeft;s.style.left=`${N}px`,s.style.maxWidth=`${U}px`}else s.style.left=`${a.offsetLeft}px`,s.style.maxWidth=`${a.offsetWidth}px`;s.style.width="8192px",f&&(s.style.transition="none"),s.offsetWidth,f&&(s.style.transition="",s.style.opacity="1")}else{if(P(["left","maxWidth","width"]),typeof U=="number"&&a.offsetHeight>=U){const N=Math.floor((a.offsetHeight-U)/2)+a.offsetTop;s.style.top=`${N}px`,s.style.maxHeight=`${U}px`}else s.style.top=`${a.offsetTop}px`,s.style.maxHeight=`${a.offsetHeight}px`;s.style.height="8192px",f&&(s.style.transition="none"),s.offsetHeight,f&&(s.style.transition="",s.style.opacity="1")}}}function te(){if(n.type==="card")return;const{value:a}=m;a&&(a.style.opacity="0")}function P(a){const{value:s}=m;if(s)for(const f of a)s.style[f]=""}function D(){if(n.type==="card")return;const a=K();a?me(a):te()}function de(a,s,f,z){const U=a.getBoundingClientRect(),J=s.getBoundingClientRect(),N=f?"left":"top",G=f?"right":"bottom";let be=0;z?be=(J[N]+J[G])/2-(U[N]+U[G])/2:J[N]<U[N]?be=J[N]-U[N]:J[G]>U[G]&&(be=J[G]-U[G]),be!==0&&a.scrollBy({[N]:be,behavior:"smooth"})}function Ie(){const a=["top","bottom"].includes(x.value),s=K();if(s)if(a){const f=T.value?.$el;if(!f)return;de(f,s,a,n.centerActiveTab)}else{const{value:f}=E;if(!f)return;de(f,s,a,n.centerActiveTab)}}const we=j(null);let Se=0,ue=null;function b(a){const s=we.value;if(s){Se=a.getBoundingClientRect().height;const f=`${Se}px`,z=()=>{s.style.height=f,s.style.maxHeight=f};ue?(z(),ue(),ue=null):ue=z}}function e(a){const s=we.value;if(s){const f=a.getBoundingClientRect().height,z=()=>{document.body.offsetHeight,s.style.maxHeight=`${f}px`,s.style.height=`${Math.max(Se,f)}px`};ue?(ue(),ue=null,z()):ue=z}}function l(){const a=we.value;if(a){a.style.maxHeight="",a.style.height="";const{paneWrapperStyle:s}=n;if(typeof s=="string")a.style.cssText=s;else if(s){const{maxHeight:f,height:z}=s;f!==void 0&&(a.style.maxHeight=f),z!==void 0&&(a.style.height=z)}}}const se={value:[]},ke=j("next");function Ve(a){const s=I.value;let f="next";for(const z of se.value){if(z===s)break;if(z===a){f="prev";break}}ke.value=f,Je(a)}function Je(a){const{onActiveNameChange:s,onUpdateValue:f,"onUpdate:value":z}=n;s&&Ue(s,a),f&&Ue(f,a),z&&Ue(z,a),ee.value=a}function ze(a){const{onClose:s}=n;s&&Ue(s,a)}function Et(a){if(["top","bottom"].includes(x.value)){const{value:s}=T;if(!s)return;const f=s.$el;if(!f)return;const z=f.offsetWidth,U=!!R?.value,J=a==="next"?z:-z;f.scrollBy({left:U?-J:J,behavior:"smooth"})}else{const{value:s}=E;if(!s)return;const f=s.offsetHeight,z=a==="next"?s.scrollTop+f:s.scrollTop-f;s.scrollTo({top:z,left:0,behavior:"smooth"})}}let qe=!0;function Xe(){const{value:a}=m;if(!a)return;qe&&(qe=!1);const s="transition-disabled";a.classList.add(s),D(),a.classList.remove(s)}const Te=j(null);function Me({transitionDisabled:a}){const s=k.value;if(!s)return;a&&s.classList.add("transition-disabled");const f=K();f&&Te.value&&(Te.value.style.width=`${f.offsetWidth}px`,Te.value.style.height=`${f.offsetHeight}px`,Te.value.style.transform=`translate(${f.offsetLeft}px, ${f.offsetTop}px)`,a&&Te.value.offsetWidth),a&&s.classList.remove("transition-disabled")}Be([I],()=>{n.type==="segment"&&Le(()=>{Me({transitionDisabled:!1})})}),At(()=>{n.type==="segment"&&Me({transitionDisabled:!0})});let it=0;function Wt(a){if(a.contentRect.width===0&&a.contentRect.height===0||it===a.contentRect.width)return;it=a.contentRect.width;const{type:s}=n;(s==="line"||s==="bar")&&(qe||n.justifyContent?.startsWith("space"))&&Xe(),s!=="segment"&&Oe(ut())}const Mt=tt(Wt,64);function dt(){const{type:a}=n;a==="line"||a==="bar"?Xe():a==="segment"&&Me({transitionDisabled:!0})}Be([()=>n.justifyContent,()=>n.size],()=>{Le(()=>{(n.type==="line"||n.type==="bar")&&Xe()})}),Be([x,()=>R?.value],()=>{Le(()=>{dt(),Oe(ut(),{instantly:!0})})}),Be(()=>n.type,()=>{Le(()=>{const a=g.value;a&&(a.classList.add("transition-disabled"),dt(),a.offsetWidth,a.classList.remove("transition-disabled"))})});const Re=j(!1);function Ot(a){const{target:s,contentRect:{width:f,height:z}}=a,U=s.parentElement.parentElement.offsetWidth,J=s.parentElement.parentElement.offsetHeight,N=x.value;if(!Re.value)N==="top"||N==="bottom"?U<f&&(Re.value=!0):J<z&&(Re.value=!0);else{const{value:G}=B;if(!G)return;N==="top"||N==="bottom"?U-f>G.$el.offsetWidth&&(Re.value=!1):J-z>G.$el.offsetHeight&&(Re.value=!1)}Oe(T.value?.$el||null)}const Ft=tt(Ot,64);function Ut(){const{onAdd:a}=n;a&&a()}const Ke=j(!1);function ut(){const a=x.value;return(a==="top"||a==="bottom"?T.value?.$el:E.value)||null}function Oe(a,s={instantly:!1}){if(!a)return;const f=s.instantly?v.value:null;f&&f.classList.add("transition-disabled");const z=1,U=x.value;if(U==="top"||U==="bottom"){const{scrollLeft:J,scrollWidth:N,offsetWidth:G}=a,be=Math.abs(J);V.value=be<=z,F.value=be+G>=N-z,Ke.value=G<N-z}else{const{scrollTop:J,scrollHeight:N,offsetHeight:G}=a;V.value=J<=z,F.value=J+G>=N-z,Ke.value=G<N-z}f&&(f.offsetWidth,f.classList.remove("transition-disabled"))}const Ht=tt(a=>{Oe(a.target)},64);Mn(rt,{triggerRef:fe(n,"trigger"),tabStyleRef:fe(n,"tabStyle"),tabClassRef:fe(n,"tabClass"),addTabStyleRef:fe(n,"addTabStyle"),addTabClassRef:fe(n,"addTabClass"),paneClassRef:fe(n,"paneClass"),paneStyleRef:fe(n,"paneStyle"),mergedClsPrefixRef:c,typeRef:fe(n,"type"),closableRef:fe(n,"closable"),valueRef:I,tabChangeIdRef:L,onBeforeLeaveRef:fe(n,"onBeforeLeave"),activateTab:Ve,handleClose:ze,handleAdd:Ut}),Gn(()=>{D(),Ie()}),Bn(()=>{const{value:a}=v;if(!a)return;const{value:s}=c,f=`${s}-tabs-nav-scroll-wrapper--shadow-start`,z=`${s}-tabs-nav-scroll-wrapper--shadow-end`;V.value?a.classList.remove(f):a.classList.add(f),F.value?a.classList.remove(z):a.classList.add(z)});const Nt={syncBarPosition:()=>{D()},scrollToCurrentTab:()=>{Ie()}},jt=()=>{Me({transitionDisabled:!0})},ct=oe(()=>{const{value:a}=ie,{type:s}=n,f=`${a}${{card:"Card",bar:"Bar",line:"Line",segment:"Segment"}[s]}`,{self:{barColor:z,closeIconColor:U,closeIconColorHover:J,closeIconColorPressed:N,tabColor:G,tabBorderColor:be,paneTextColor:Dt,tabFontWeight:Vt,tabBorderRadius:Jt,tabFontWeightActive:qt,colorSegment:Xt,fontWeightStrong:Kt,tabColorSegment:Gt,closeSize:Qt,closeIconSize:Yt,closeColorHover:Zt,closeColorPressed:en,closeBorderRadius:tn,[X("panePadding",a)]:Fe,[X("tabPadding",f)]:nn,[X("tabPaddingVertical",f)]:an,[X("tabGap",f)]:ln,[X("tabGap",`${f}Vertical`)]:on,[X("tabTextColor",s)]:sn,[X("tabTextColorActive",s)]:rn,[X("tabTextColorHover",s)]:dn,[X("tabTextColorDisabled",s)]:un,[X("tabFontSize",a)]:cn},common:{cubicBezierEaseInOut:bn}}=S.value;return{"--n-bezier":bn,"--n-color-segment":Xt,"--n-bar-color":z,"--n-tab-font-size":cn,"--n-tab-text-color":sn,"--n-tab-text-color-active":rn,"--n-tab-text-color-disabled":un,"--n-tab-text-color-hover":dn,"--n-pane-text-color":Dt,"--n-tab-border-color":be,"--n-tab-border-radius":Jt,"--n-close-size":Qt,"--n-close-icon-size":Yt,"--n-close-color-hover":Zt,"--n-close-color-pressed":en,"--n-close-border-radius":tn,"--n-close-icon-color":U,"--n-close-icon-color-hover":J,"--n-close-icon-color-pressed":N,"--n-tab-color":G,"--n-tab-font-weight":Vt,"--n-tab-font-weight-active":qt,"--n-tab-padding":nn,"--n-tab-padding-vertical":an,"--n-tab-gap":ln,"--n-tab-gap-vertical":on,"--n-pane-padding-left":We(Fe,"left"),"--n-pane-padding-right":We(Fe,"right"),"--n-pane-padding-top":We(Fe,"top"),"--n-pane-padding-bottom":We(Fe,"bottom"),"--n-font-weight-strong":Kt,"--n-tab-color-segment":Gt}}),bt=w?$t("tabs",oe(()=>`${ie.value[0]}${n.type[0]}`),ct,n):void 0;return{mergedClsPrefix:c,mergedValue:I,renderedNames:new Set,segmentCapsuleElRef:Te,tabsPaneWrapperRef:we,tabsElRef:k,selfElRef:g,barElRef:m,addTabInstRef:B,xScrollInstRef:T,scrollWrapperElRef:v,addTabFixed:Re,tabWrapperStyle:Q,handleNavResize:Mt,mergedSize:ie,handleScroll:Ht,handleTabsResize:Ft,cssVars:w?void 0:ct,themeClass:bt?.themeClass,animationDirection:ke,renderNameListRef:se,yScrollElRef:E,handleSegmentResize:jt,onAnimationBeforeLeave:b,onAnimationEnter:e,onAnimationAfterEnter:l,onRender:bt?.onRender,startReachedRef:V,endReachedRef:F,isOverflow:Ke,handleButtonClick:Et,mergedTheme:S,rtlEnabled:R,mergedPlacement:x,...Nt}},render(){const{mergedClsPrefix:n,type:d,mergedPlacement:c,addTabFixed:w,addable:p,mergedSize:C,renderNameListRef:R,onRender:x,paneWrapperClass:S,paneWrapperStyle:k,startReachedRef:g,endReachedRef:m,isOverflow:v,showScrollButton:B,handleButtonClick:T,mergedTheme:E,rtlEnabled:V,$slots:{default:F,prefix:Z,suffix:ie}}=this;x?.();const Y=F?Ge(F()).filter(P=>P.type.__TAB_PANE__===!0):[],ee=F?Ge(F()).filter(P=>P.type.__TAB__===!0):[],I=!ee.length,L=d==="card",Q=d==="segment",K=!L&&!Q&&this.justifyContent;R.value=[];const me=()=>{const P=(u(),y("div",{style:Ce(this.tabWrapperStyle),class:A(`${n}-tabs-wrapper`)},[K?$(()=>null):(u(),y("div",{key:1,class:A(`${n}-tabs-scroll-padding`),style:Ce(c==="top"||c==="bottom"?{width:`${this.tabsPadding}px`}:{height:`${this.tabsPadding}px`})},null,6)),I?(u(),y(ae,{key:2},[$(()=>Y.map((D,de)=>(R.value.push(D.props.name),nt((u(),H(lt,$e(D.props,{internalCreatedByPane:!0,internalLeftPadded:de!==0&&(!K||K==="center"||K==="start"||K==="end")}),ft(D.children?{default:D.children.tab}:void 0),1040,["internalLeftPadded"]))))))],64)):(u(),y(ae,{key:3},[$(()=>ee.map((D,de)=>(R.value.push(D.props.name),nt(de!==0&&!K?zt(D):D))))],64)),!w&&p&&L?(u(),y(ae,{key:4},[$(()=>kt(p,(I?Y.length:ee.length)!==0))],64)):$(()=>null),K?$(()=>null):(u(),y("div",{key:7,class:A(`${n}-tabs-scroll-padding`),style:Ce({width:`${this.tabsPadding}px`})},null,6)),L?$(()=>null):(u(),y("div",{key:9,ref:"barElRef",class:A(`${n}-tabs-bar`)},null,2))],6));return u(),y("div",{ref:"tabsElRef",class:A(`${n}-tabs-nav-scroll-content`)},[L&&p?(u(),H(Qe,{key:0,onResize:this.handleTabsResize},{default:()=>P},1032,["onResize"])):(u(),y(ae,{key:1},[$(()=>P)],64)),L?(u(),y("div",{key:2,class:A(`${n}-tabs-pad`)},null,2)):$(()=>null)],2)},te=Q?"top":c;return u(),y("div",{ref:"selfElRef",class:A([`${n}-tabs`,this.themeClass,`${n}-tabs--${d}-type`,`${n}-tabs--${C}-size`,K&&`${n}-tabs--flex`,`${n}-tabs--${te}`,V&&`${n}-tabs--rtl`]),style:Ce(this.cssVars)},[t("div",{class:A([`${n}-tabs-nav--${d}-type`,`${n}-tabs-nav--${te}`,`${n}-tabs-nav`])},[$(()=>at(Z,P=>P&&(u(),y("div",{class:A(`${n}-tabs-nav__prefix`)},[$(()=>P)],2)))),Q?(u(),H(Qe,{key:0,onResize:this.handleSegmentResize},{default:()=>(u(),y("div",{class:A(`${n}-tabs-rail`),ref:"tabsElRef"},[t("div",{class:A(`${n}-tabs-capsule`),ref:"segmentCapsuleElRef"},[t("div",{class:A(`${n}-tabs-wrapper`)},[t("div",{class:A(`${n}-tabs-tab`)},null,2)],2)],2),I?(u(),y(ae,{key:0},[$(()=>Y.map((P,D)=>(R.value.push(P.props.name),u(),H(lt,$e(P.props,{internalCreatedByPane:!0,internalLeftPadded:D!==0}),ft(P.children?{default:P.children.tab}:void 0),1040,["internalLeftPadded"]))))],64)):(u(),y(ae,{key:1},[$(()=>ee.map((P,D)=>(R.value.push(P.props.name),D===0?P:zt(P))))],64))],2))},1032,["onResize"])):(u(),y(ae,{key:1},[$(()=>B&&v&&(u(),H(wt,{mergedClsPrefix:n,type:"prev",vertical:te==="left"||te==="right",disabled:g,rtl:!!V,theme:E.peers.Button,themeOverrides:E.peerOverrides.Button,onClick:T},null,8,["mergedClsPrefix","vertical","disabled","rtl","theme","themeOverrides","onClick"]))),(u(),H(Qe,{onResize:this.handleNavResize},{default:()=>(u(),y("div",{class:A(`${n}-tabs-nav-scroll-wrapper`),ref:"scrollWrapperElRef"},[["top","bottom"].includes(te)?(u(),H(Sa,{key:0,ref:"xScrollInstRef",onScroll:this.handleScroll},{default:me},1032,["onScroll"])):(u(),y("div",{key:1,class:A(`${n}-tabs-nav-y-scroll`),onScroll:this.handleScroll,ref:"yScrollElRef"},[$(()=>me())],42,["onScroll"]))],2))},1032,["onResize"])),$(()=>B&&v&&(u(),H(wt,{mergedClsPrefix:n,type:"next",vertical:te==="left"||te==="right",disabled:m,rtl:!!V,theme:E.peers.Button,themeOverrides:E.peerOverrides.Button,onClick:T},null,8,["mergedClsPrefix","vertical","disabled","rtl","theme","themeOverrides","onClick"])))],64)),w&&p&&L?(u(),y(ae,{key:2},[$(()=>kt(p,!0))],64)):$(()=>null),$(()=>at(ie,P=>P&&(u(),y("div",{class:A(`${n}-tabs-nav__suffix`)},[$(()=>P)],2))))],2),$(()=>I&&(this.animated&&(te==="top"||te==="bottom")?(u(),y("div",{key:1,ref:"tabsPaneWrapperRef",style:Ce(k),class:A([`${n}-tabs-pane-wrapper`,S])},[$(()=>St(Y,this.mergedValue,this.renderedNames,this.onAnimationBeforeLeave,this.onAnimationEnter,this.onAnimationAfterEnter,this.animationDirection))],6)):St(Y,this.mergedValue,this.renderedNames)))],6)}});function St(n,d,c,w,p,C,R){const x=[];return n.forEach(S=>{const{name:k,displayDirective:g,"display-directive":m}=S.props,v=T=>g===T||m===T,B=d===k;if(S.key!==void 0&&(S.key=k),B||v("show")||v("show:lazy")&&c.has(k)){c.has(k)||c.add(k);const T=!v("if");x.push(T?An(S,[[_n,B]]):S)}}),R?(u(),H(En,{name:`${R}-transition`,onBeforeLeave:w,onEnter:p,onAfterEnter:C},{default:()=>x},1032,["name","onBeforeLeave","onEnter","onAfterEnter"])):x}function kt(n,d){return u(),H(lt,{ref:"addTabInstRef",key:"__addable",name:"__addable",internalCreatedByPane:!0,internalAddable:!0,internalLeftPadded:d,disabled:typeof n=="object"&&n.disabled},null,8,["internalLeftPadded","disabled"])}function zt(n){const d=Wn(n);return d.props?d.props.internalLeftPadded=!0:d.props={internalLeftPadded:!0},d}function nt(n){return Array.isArray(n.dynamicProps)?n.dynamicProps.includes("internalLeftPadded")||n.dynamicProps.push("internalLeftPadded"):n.dynamicProps=["internalLeftPadded"],n}/**
 * @license lucide-vue-next v0.468.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Tt=st("BracesIcon",[["path",{d:"M8 3H7a2 2 0 0 0-2 2v5a2 2 0 0 1-2 2 2 2 0 0 1 2 2v5c0 1.1.9 2 2 2h1",key:"ezmyqa"}],["path",{d:"M16 21h1a2 2 0 0 0 2-2v-5c0-1.1.9-2 2-2a2 2 0 0 1-2-2V5a2 2 0 0 0-2-2h-1",key:"e1hn23"}]]);/**
 * @license lucide-vue-next v0.468.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Wa=st("MonitorCogIcon",[["path",{d:"M12 17v4",key:"1riwvh"}],["path",{d:"m15.2 4.9-.9-.4",key:"12wd2u"}],["path",{d:"m15.2 7.1-.9.4",key:"1r2vl7"}],["path",{d:"m16.9 3.2-.4-.9",key:"3zbo91"}],["path",{d:"m16.9 8.8-.4.9",key:"1qr2dn"}],["path",{d:"m19.5 2.3-.4.9",key:"1rjrkq"}],["path",{d:"m19.5 9.7-.4-.9",key:"heryx5"}],["path",{d:"m21.7 4.5-.9.4",key:"17fqt1"}],["path",{d:"m21.7 7.5-.9-.4",key:"14zyni"}],["path",{d:"M22 13v2a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h7",key:"1tnzv8"}],["path",{d:"M8 21h8",key:"1ev6f3"}],["circle",{cx:"18",cy:"6",r:"3",key:"1h7g24"}]]);/**
 * @license lucide-vue-next v0.468.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const Ma=st("PaletteIcon",[["circle",{cx:"13.5",cy:"6.5",r:".5",fill:"currentColor",key:"1okk4w"}],["circle",{cx:"17.5",cy:"10.5",r:".5",fill:"currentColor",key:"f64h9f"}],["circle",{cx:"8.5",cy:"7.5",r:".5",fill:"currentColor",key:"fotxhn"}],["circle",{cx:"6.5",cy:"12.5",r:".5",fill:"currentColor",key:"qy21gx"}],["path",{d:"M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10c.926 0 1.648-.746 1.648-1.688 0-.437-.18-.835-.437-1.125-.29-.289-.438-.652-.438-1.125a1.64 1.64 0 0 1 1.668-1.668h1.996c3.051 0 5.555-2.503 5.555-5.554C21.965 6.012 17.461 2 12 2z",key:"12rzf8"}]]),Oa={class:"page settings-page"},Fa={class:"page-scroll settings-scroll"},Ua={class:"panel settings-panel"},Ha={class:"panel-head"},Na={class:"theme-cards"},ja=["onClick"],Da={class:"theme-name"},Va={key:0,class:"theme-check"},Ja={class:"panel settings-panel"},qa={class:"panel-head"},Xa={class:"setting-row"},Ka={class:"setting-row"},Ga={class:"setting-row"},Qa={class:"slider-inline"},Ya={class:"setting-row"},Za={class:"panel settings-panel"},el={class:"panel-head"},tl={key:0},nl={class:"cover-setting"},al=["src"],ll={key:1},ol={class:"cover-actions"},sl={class:"page-scroll settings-scroll"},rl={class:"panel settings-panel"},il={class:"panel-head"},dl={class:"setting-grid"},ul={class:"setting-field"},cl={class:"setting-field"},bl={class:"setting-field"},fl={class:"setting-field"},vl={class:"setting-field"},pl={class:"setting-field"},gl={class:"slider-inline"},ml={class:"setting-field"},hl={class:"setting-field"},xl={class:"slider-inline"},yl={class:"panel settings-panel"},Cl={class:"panel-head"},wl={class:"setting-grid"},Sl={class:"setting-field"},kl={class:"setting-field"},zl={class:"setting-field"},Tl={class:"setting-field"},Rl={class:"setting-field"},Pl={class:"slider-inline"},$l={class:"setting-field"},Il={class:"slider-inline"},Bl={class:"setting-grid margins"},Ll={class:"setting-field"},Al={class:"setting-field"},_l={class:"setting-field"},El={class:"setting-field"},Wl={class:"setting-grid"},Ml={class:"setting-field"},Ol={class:"setting-field"},Fl={class:"setting-field"},Ul={class:"setting-field"},Hl={class:"slider-inline"},Nl={class:"settings-save-row"},jl={class:"page-scroll settings-scroll"},Dl={class:"panel settings-panel"},Vl={class:"panel-head"},Jl={class:"setting-row"},ql={class:"setting-label"},Xl={class:"mono"},Kl={class:"row-actions"},Gl={class:"setting-row"},Ql={class:"panel settings-panel"},Yl={class:"panel-head"},Zl={class:"maintain-row"},eo={class:"maintain-row"},to={class:"maintain-row"},no={key:0,class:"job-box"},ao={class:"job-head"},lo={key:0},oo={class:"panel settings-panel"},so={class:"panel-head"},ro={class:"setting-row"},io={class:"setting-row"},uo={class:"page-scroll settings-scroll"},co={class:"panel settings-panel"},bo={class:"panel-head"},fo={class:"setting-grid"},vo={class:"setting-field"},po={class:"slider-inline"},go={class:"setting-field"},mo={class:"setting-field"},ho={class:"setting-field"},xo={class:"slider-inline"},yo=["title"],Co={class:"setting-field"},wo={class:"setting-field"},So={class:"setting-field"},ko={class:"setting-field"},zo={class:"panel settings-panel"},To={class:"panel-head"},Ro={class:"setting-grid"},Po={class:"setting-field"},$o={class:"slider-inline"},Io={class:"setting-field"},Bo={class:"setting-field"},Lo={class:"page-scroll settings-scroll"},Ao={class:"panel about-panel"},_o={class:"about-grid"},Eo={class:"mono"},Wo={class:"about-actions"},Mo={class:"about-note"},Zo=ge({name:"SettingsView",__name:"SettingsView",setup(n){const d=j("appearance"),c=j({}),w=j(!0),p=j(!1),C=j(null);let R=null;const x=j(Date.now()),S=j(null),k={},g=oe(()=>re.settings),m=b=>c.value?.globalViewSettings?.[b]??gt[b];function v(b,e){c.value.globalViewSettings||(c.value.globalViewSettings={}),c.value.globalViewSettings[b]=e}const B=oe(()=>{const b=re.fonts.embedded||[],e=re.fonts.fonts||[];return[...b.map(se=>({label:`${se}（内置）`,value:se})),...e.map(se=>({label:se,value:se}))]}),T=[{label:"无衬线（Sans-Serif Font）",value:"Sans-Serif Font"},{label:"衬线（Serif Font）",value:"Serif Font"},{label:"等宽（Monospace Font）",value:"Monospace Font"}],E=[{label:"浅色纸张",value:"light"},{label:"深色夜间",value:"dark"}],V=oe(()=>m("scrolled")?"scroll":Number(m("maxColumnCount"))>=2?"double":"single");function F(b){b==="scroll"?(v("scrolled",!0),v("continuousScroll",!0),v("maxColumnCount",1)):b==="double"?(v("scrolled",!1),v("continuousScroll",!1),v("maxColumnCount",2)):(v("scrolled",!1),v("continuousScroll",!1),v("maxColumnCount",1))}const Z=[{label:"单页翻页",value:"single"},{label:"双页翻页",value:"double"},{label:"连续滚动",value:"scroll"}];async function ie(){try{const b=await ve.readerSettings(),e=JSON.parse(b.json||"{}");e.globalViewSettings||(e.globalViewSettings={}),c.value=e}catch(b){_e(b)}}async function Y(){p.value=!0;try{const b=c.value.globalViewSettings||{};c.value.themeMode=b.theme==="dark"?"dark":"light",c.value.language||(c.value.language="zh-CN");const e=JSON.stringify(c.value);await ve.saveReaderSettings(e),await pe({readerSettingsJson:e}),Pe("排版设置已保存，新打开的阅读器会立即使用")}catch(b){_e(b)}finally{p.value=!1}}async function ee(){await Ye({title:"恢复默认排版",content:"将字号、行距、页边距、字体等恢复为默认值。",positiveText:"恢复"})&&(c.value={version:1,language:"zh-CN",themeMode:"light",globalViewSettings:{...gt}},await Y())}function I(b,e){k[b]&&clearTimeout(k[b]),k[b]=setTimeout(()=>{pe(e)},260)}function L(b,e){pe({media:{...g.value.media,[b]:e}})}const Q=oe(()=>g.value.media.imageBackgroundMusic?Jn(g.value.media.imageBackgroundMusic):"");async function K(){const b=await jn.pickFile("audio","选择幻灯片背景音乐");b&&L("imageBackgroundMusic",b)}function me(b){pe({appTheme:b}),Pe(`已切换主题：${He[b]?.label??b}`)}async function te(b){const e=b.target,l=e.files?.[0];if(e.value="",!!l)try{const se=await new Promise((Ve,Je)=>{const ze=new FileReader;ze.onload=()=>Ve(String(ze.result||"")),ze.onerror=()=>Je(new Error("读取图片失败")),ze.readAsDataURL(l)}),ke=await ve.setDefaultCover(se);if(!ke.ok)throw new Error(ke.error||"设置失败");Pe(`缺省封面已保存，将用于 ${ke.applied} 本无封面书籍`),await mt(!0),x.value=Date.now()}catch(se){_e(se)}}const P=oe(()=>g.value.defaultCoverPath?`/api/defaultcover?t=${x.value}`:"");async function D(b){if(!C.value?.running&&!(b==="purge"&&!await Ye({title:"清理失效记录",content:"只移除文件已不存在的书架记录，不删除任何文件。确定继续吗？",positiveText:"开始清理"})))try{C.value=b==="covers"?await ve.rebuildCovers():b==="purge"?await ve.purgeMissing():await ve.repairMetadata(),Pe(b==="covers"?"已开始重建封面":b==="purge"?"已开始清理失效记录":"已开始补齐元数据"),de()}catch(e){_e(e)}}function de(){C.value&&(R&&clearTimeout(R),R=setTimeout(async()=>{if(C.value)try{const b=await ve.job(C.value.id);C.value=b,b.running?de():(Pe(b.message||"任务完成"),Dn(100))}catch{}},800))}const Ie=oe(()=>{const b=C.value;return b?b.total?Math.max(2,Math.min(100,Math.round(b.done/b.total*100))):5:0});async function we(){if(await Ye({title:"清理旧版 JSON 备份",content:"旧版 books/configs/appsettings JSON 已全部迁移到 SQLite。删除后不可恢复；不会删除原书文件、封面或当前数据库。确定删除吗？",positiveText:"删除备份"}))try{const e=await ve.cleanupLegacy();if(!e.ok)throw new Error(e.error||"清理失败");Pe(`已删除 ${e.count} 个旧版文件（${Vn(Number(e.message||0))}）`)}catch(e){_e(e)}}function Se(){const b=re.appInfo?.dataDir;b&&ve.fsOpen(b+"\\logs")}function ue(){const b=re.appInfo?.dataDir;b&&ve.fsOpen(b)}return At(async()=>{w.value=!0,await Promise.all([mt(),qn(),Xn(),ie()]),w.value=!1}),On(()=>{R&&clearTimeout(R);for(const b of Object.values(k))clearTimeout(b)}),(b,e)=>(u(),y("div",Oa,[i(aa,{title:"设置",subtitle:"外观、阅读排版、书库与媒体偏好"}),i(o(Ea),{value:d.value,"onUpdate:value":e[42]||(e[42]=l=>d.value=l),type:"line",animated:"",class:"settings-tabs"},{default:M(()=>[i(o(Ee),{name:"appearance",tab:"外观"},{default:M(()=>[t("div",Fa,[t("section",Ua,[t("div",Ha,[t("h2",null,[i(o(Ma),{size:15}),e[43]||(e[43]=W("界面主题",-1))]),e[44]||(e[44]=t("span",null,"5 套主题，立即生效",-1))]),t("div",Na,[(u(!0),y(ae,null,Fn(o(Un),l=>(u(),y("button",{key:l,class:vt(["theme-card",{active:g.value.appTheme===l}]),onClick:se=>me(l)},[t("span",{class:"theme-swatch",style:Ce({background:`linear-gradient(135deg, ${o(He)[l].panel}, ${o(He)[l].accent})`})},null,4),t("span",Da,q(o(He)[l].label),1),g.value.appTheme===l?(u(),y("span",Va,"●")):Ae("",!0)],10,ja))),128))])]),t("section",Ja,[t("div",qa,[t("h2",null,[i(o(Wa),{size:15}),e[45]||(e[45]=W("书架显示",-1))])]),t("div",Xa,[e[46]||(e[46]=t("div",{class:"setting-label"},[t("strong",null,"显示封面"),t("span",null,"关闭后书架使用渐变书脊占位")],-1)),i(o(ne),{value:g.value.showCovers,"onUpdate:value":e[0]||(e[0]=l=>o(pe)({showCovers:l}))},null,8,["value"])]),t("div",Ka,[e[47]||(e[47]=t("div",{class:"setting-label"},[t("strong",null,"默认排序"),t("span",null,"书架打开时使用的排序方式")],-1)),i(o(ce),{value:g.value.sortMode,style:{width:"170px"},options:[{label:"最近阅读",value:"recent"},{label:"导入时间",value:"added"},{label:"书名",value:"title"},{label:"作者",value:"author"},{label:"文件大小",value:"size"},{label:"格式",value:"format"}],"onUpdate:value":e[1]||(e[1]=l=>o(pe)({sortMode:l}))},null,8,["value"])]),t("div",Ga,[e[48]||(e[48]=t("div",{class:"setting-label"},[t("strong",null,"封面宽度"),t("span",null,"控制书架每列的书封大小")],-1)),t("div",Qa,[i(o(ye),{value:g.value.gridSize,min:110,max:260,step:4,style:{width:"200px"},"onUpdate:value":e[2]||(e[2]=l=>{g.value.gridSize=l,I("grid",{gridSize:l})})},null,8,["value"]),t("b",null,q(g.value.gridSize)+"px",1)])]),t("div",Ya,[e[49]||(e[49]=t("div",{class:"setting-label"},[t("strong",null,"无封面时显示缺省封面"),t("span",null,"EPUB / PDF / MOBI / AZW3 没有提取到封面时使用")],-1)),i(o(ne),{value:g.value.useDefaultCover,"onUpdate:value":e[3]||(e[3]=l=>o(pe)({useDefaultCover:l}))},null,8,["value"])])]),t("section",Za,[t("div",el,[t("h2",null,[i(o(Ze),{size:15}),e[50]||(e[50]=W("缺省封面",-1))]),g.value.defaultCoverPath?(u(),y("span",tl,"已设置")):Ae("",!0)]),t("div",nl,[t("div",{class:vt(["cover-preview",{empty:!g.value.defaultCoverPath}])},[g.value.defaultCoverPath?(u(),y("img",{key:0,src:P.value,alt:""},null,8,al)):(u(),y("span",ll,"未设置"))],2),t("div",ol,[i(o(le),{onClick:e[4]||(e[4]=l=>S.value?.click())},{icon:M(()=>[i(o(Ze),{size:15})]),default:M(()=>[e[51]||(e[51]=W("选择图片…",-1))]),_:1}),e[52]||(e[52]=t("p",null,"用于无提取封面的书；TXT / Markdown 永远使用渐变占位。",-1)),t("input",{ref_key:"coverInput",ref:S,type:"file",accept:"image/png,image/jpeg,image/webp",hidden:"",onChange:te},null,544)])])])])]),_:1}),i(o(Ee),{name:"reading",tab:"阅读排版"},{default:M(()=>[t("div",sl,[i(o(Pa),{type:"info",bordered:!1,class:"settings-alert"},{default:M(()=>[...e[53]||(e[53]=[W(" 以下设置写入 readest 的 ",-1),t("code",null,"globalViewSettings",-1),W("，新打开的阅读器会立即应用；已打开的阅读器可在其侧栏中单独调整。 ",-1)])]),_:1}),t("section",rl,[t("div",il,[t("h2",null,[i(o(la),{size:15}),e[54]||(e[54]=W("字体",-1))])]),t("div",dl,[t("label",ul,[e[55]||(e[55]=t("span",null,"中文字体",-1)),i(o(ce),{value:m("defaultCJKFont"),options:B.value,filterable:"","onUpdate:value":e[5]||(e[5]=l=>v("defaultCJKFont",l))},null,8,["value","options"])]),t("label",cl,[e[56]||(e[56]=t("span",null,"西文字体",-1)),i(o(ce),{value:m("defaultFont"),options:T,"onUpdate:value":e[6]||(e[6]=l=>v("defaultFont",l))},null,8,["value"])]),t("label",bl,[e[57]||(e[57]=t("span",null,"衬线字体名",-1)),i(o(ce),{value:m("serifFont"),options:B.value,filterable:"","onUpdate:value":e[7]||(e[7]=l=>v("serifFont",l))},null,8,["value","options"])]),t("label",fl,[e[58]||(e[58]=t("span",null,"无衬线字体名",-1)),i(o(ce),{value:m("sansSerifFont"),options:B.value,filterable:"","onUpdate:value":e[8]||(e[8]=l=>v("sansSerifFont",l))},null,8,["value","options"])]),t("label",vl,[e[59]||(e[59]=t("span",null,"等宽字体名",-1)),i(o(ce),{value:m("monospaceFont"),options:B.value,filterable:"","onUpdate:value":e[9]||(e[9]=l=>v("monospaceFont",l))},null,8,["value","options"])]),t("label",pl,[e[60]||(e[60]=t("span",null,"字号",-1)),t("div",gl,[i(o(ye),{value:Number(m("defaultFontSize")),min:12,max:40,step:1,style:{width:"150px"},"onUpdate:value":e[10]||(e[10]=l=>v("defaultFontSize",l))},null,8,["value"]),t("b",null,q(m("defaultFontSize"))+"px",1)])]),t("label",ml,[e[61]||(e[61]=t("span",null,"字重",-1)),i(o(ce),{value:Number(m("fontWeight")),options:[{label:"300 细",value:300},{label:"400 常规",value:400},{label:"500 中等",value:500},{label:"600 半粗",value:600},{label:"700 粗体",value:700}],"onUpdate:value":e[11]||(e[11]=l=>v("fontWeight",l))},null,8,["value"])]),t("label",hl,[e[62]||(e[62]=t("span",null,"行距",-1)),t("div",xl,[i(o(ye),{value:Number(m("lineHeight")),min:1,max:3,step:.05,style:{width:"150px"},"onUpdate:value":e[12]||(e[12]=l=>v("lineHeight",String(l)))},null,8,["value"]),t("b",null,q(m("lineHeight")),1)])])])]),t("section",yl,[t("div",Cl,[t("h2",null,[i(o(Hn),{size:15}),e[63]||(e[63]=W("版式与主题",-1))])]),t("div",wl,[t("label",Sl,[e[64]||(e[64]=t("span",null,"阅读主题",-1)),i(o(ce),{value:m("theme"),options:E,"onUpdate:value":e[13]||(e[13]=l=>v("theme",l))},null,8,["value"])]),t("label",kl,[e[65]||(e[65]=t("span",null,"翻页方式",-1)),i(o(ce),{value:V.value,options:Z,"onUpdate:value":F},null,8,["value"])]),t("label",zl,[e[66]||(e[66]=t("span",null,"两端对齐",-1)),i(o(ne),{value:!!m("fullJustification"),"onUpdate:value":e[14]||(e[14]=l=>v("fullJustification",l))},null,8,["value"])]),t("label",Tl,[e[67]||(e[67]=t("span",null,"自动断字",-1)),i(o(ne),{value:!!m("hyphenation"),"onUpdate:value":e[15]||(e[15]=l=>v("hyphenation",l))},null,8,["value"])]),t("label",Rl,[e[68]||(e[68]=t("span",null,"段间距",-1)),t("div",Pl,[i(o(ye),{value:Number(m("paragraphMargin")),min:0,max:3,step:.1,style:{width:"150px"},"onUpdate:value":e[16]||(e[16]=l=>v("paragraphMargin",l))},null,8,["value"]),t("b",null,q(m("paragraphMargin")),1)])]),t("label",$l,[e[69]||(e[69]=t("span",null,"首行缩进",-1)),t("div",Il,[i(o(ye),{value:Number(m("textIndent")),min:0,max:4,step:.5,style:{width:"150px"},"onUpdate:value":e[17]||(e[17]=l=>v("textIndent",l))},null,8,["value"]),t("b",null,q(m("textIndent"))+"em",1)])])]),t("div",Bl,[t("label",Ll,[e[70]||(e[70]=t("span",null,"上边距",-1)),i(o(Ne),{value:Number(m("marginTopPx")),min:0,max:200,"onUpdate:value":e[18]||(e[18]=l=>v("marginTopPx",l??0))},null,8,["value"])]),t("label",Al,[e[71]||(e[71]=t("span",null,"下边距",-1)),i(o(Ne),{value:Number(m("marginBottomPx")),min:0,max:200,"onUpdate:value":e[19]||(e[19]=l=>v("marginBottomPx",l??0))},null,8,["value"])]),t("label",_l,[e[72]||(e[72]=t("span",null,"左边距",-1)),i(o(Ne),{value:Number(m("marginLeftPx")),min:0,max:200,"onUpdate:value":e[20]||(e[20]=l=>v("marginLeftPx",l??0))},null,8,["value"])]),t("label",El,[e[73]||(e[73]=t("span",null,"右边距",-1)),i(o(Ne),{value:Number(m("marginRightPx")),min:0,max:200,"onUpdate:value":e[21]||(e[21]=l=>v("marginRightPx",l??0))},null,8,["value"])])]),t("div",Wl,[t("label",Ml,[e[74]||(e[74]=t("span",null,"显示页眉",-1)),i(o(ne),{value:!!m("showHeader"),"onUpdate:value":e[22]||(e[22]=l=>v("showHeader",l))},null,8,["value"])]),t("label",Ol,[e[75]||(e[75]=t("span",null,"显示页脚 / 页码",-1)),i(o(ne),{value:!!m("showPageNumber"),"onUpdate:value":e[23]||(e[23]=l=>v("showPageNumber",l))},null,8,["value"])]),t("label",Fl,[e[76]||(e[76]=t("span",null,"翻页动画",-1)),i(o(ne),{value:!!m("animated"),"onUpdate:value":e[24]||(e[24]=l=>v("animated",l))},null,8,["value"])]),t("label",Ul,[e[77]||(e[77]=t("span",null,"朗读语速",-1)),t("div",Hl,[i(o(ye),{value:Number(m("ttsRate")??1.3),min:.5,max:2.5,step:.1,style:{width:"150px"},"onUpdate:value":e[25]||(e[25]=l=>v("ttsRate",l))},null,8,["value"]),t("b",null,q(Number(m("ttsRate")??1.3).toFixed(1))+"×",1)])])]),t("div",Nl,[i(o(le),{onClick:ee},{icon:M(()=>[i(o(ea),{size:15})]),default:M(()=>[e[78]||(e[78]=W("恢复默认",-1))]),_:1}),i(o(le),{type:"primary",loading:p.value,onClick:Y},{icon:M(()=>[i(o(oa),{size:15})]),default:M(()=>[e[79]||(e[79]=W("保存排版设置",-1))]),_:1},8,["loading"])])])])]),_:1}),i(o(Ee),{name:"library",tab:"书库与数据"},{default:M(()=>[t("div",jl,[t("section",Dl,[t("div",Vl,[t("h2",null,[i(o(Yn),{size:15}),e[80]||(e[80]=W("SQLite 数据",-1))]),e[81]||(e[81]=t("span",null,"原生 AOT 实测可用",-1))]),t("div",Jl,[t("div",ql,[e[82]||(e[82]=t("strong",null,"数据目录",-1)),t("span",Xl,q(o(re).appInfo?.dataDir),1)]),t("div",Kl,[i(o(le),{size:"small",onClick:ue},{icon:M(()=>[i(o(pt),{size:14})]),default:M(()=>[e[83]||(e[83]=W("打开",-1))]),_:1}),i(o(le),{size:"small",onClick:Se},{icon:M(()=>[i(o(Tt),{size:14})]),default:M(()=>[e[84]||(e[84]=W("日志",-1))]),_:1})])]),e[87]||(e[87]=t("p",{class:"setting-note"},[W("书库、进度、笔记、设置、播放列表保存在 "),t("code",null,"moonaot.db"),W("；封面/缩略图/导入副本位于同目录。旧的 "),t("code",null,"%LOCALAPPDATA%\\MoonAot"),W(" JSON 已迁移并保留作为备份，可在确认无误后删除。")],-1)),t("div",Gl,[e[86]||(e[86]=t("div",{class:"setting-label"},[t("strong",null,"清理旧版 JSON 备份"),t("span",null,"删除 %LOCALAPPDATA%\\MoonAot\\data 下已迁移的 JSON（原书/封面不受影响）")],-1)),i(o(le),{size:"small",type:"error",ghost:"",onClick:we},{default:M(()=>[...e[85]||(e[85]=[W("清理备份",-1)])]),_:1})])]),t("section",Ql,[t("div",Yl,[t("h2",null,[i(o(sa),{size:15}),e[88]||(e[88]=W("缓存维护",-1))])]),t("div",Zl,[e[90]||(e[90]=t("div",{class:"maintain-info"},[t("strong",null,"重建封面"),t("p",null,"重新提取 EPUB / MOBI / PDF 封面并压到 ≤48KB；书架与详情直接使用该封面，不再保存第二份缩略图。")],-1)),i(o(le),{disabled:!!C.value?.running,onClick:e[26]||(e[26]=l=>D("covers"))},{default:M(()=>[...e[89]||(e[89]=[W("重建封面",-1)])]),_:1},8,["disabled"])]),t("div",eo,[e[92]||(e[92]=t("div",{class:"maintain-info"},[t("strong",null,"补齐书籍元数据"),t("p",null,"从 EPUB / MOBI / PDF 重新读取作者、出版社、简介，只填空字段。")],-1)),i(o(le),{disabled:!!C.value?.running,onClick:e[27]||(e[27]=l=>D("metadata"))},{default:M(()=>[...e[91]||(e[91]=[W("补齐元数据",-1)])]),_:1},8,["disabled"])]),t("div",to,[e[94]||(e[94]=t("div",{class:"maintain-info"},[t("strong",null,"清理失效记录"),t("p",null,"仅移除文件已不存在的记录，不删除原文件。")],-1)),i(o(le),{disabled:!!C.value?.running,type:"error",ghost:"",onClick:e[28]||(e[28]=l=>D("purge"))},{default:M(()=>[...e[93]||(e[93]=[W("清理失效",-1)])]),_:1},8,["disabled"])]),C.value?(u(),y("div",no,[t("div",ao,[t("span",null,q(C.value.running?"任务进行中…":"任务完成"),1),t("span",null,q(C.value.done)+" / "+q(C.value.total),1)]),i(o(Zn),{type:"line",percentage:Ie.value,"show-indicator":!1,height:6},null,8,["percentage"]),C.value.message?(u(),y("p",lo,q(C.value.message),1)):Ae("",!0)])):Ae("",!0)]),t("section",oo,[t("div",so,[t("h2",null,[i(o(Nn),{size:15}),e[95]||(e[95]=W("导入与刷新",-1))])]),t("div",ro,[e[96]||(e[96]=t("div",{class:"setting-label"},[t("strong",null,"阅读器进度自动刷新书架"),t("span",null,"阅读时书架上的进度与最近阅读会同步更新")],-1)),i(o(ne),{value:g.value.autoRefreshLibrary,"onUpdate:value":e[29]||(e[29]=l=>o(pe)({autoRefreshLibrary:l}))},null,8,["value"])]),t("div",io,[e[97]||(e[97]=t("div",{class:"setting-label"},[t("strong",null,"导入时提取封面"),t("span",null,"单本导入时立即生成封面缩略图；批量导入按需生成")],-1)),i(o(ne),{value:g.value.importCovers,"onUpdate:value":e[30]||(e[30]=l=>o(pe)({importCovers:l}))},null,8,["value"])])])])]),_:1}),i(o(Ee),{name:"media",tab:"媒体"},{default:M(()=>[t("div",uo,[t("section",co,[t("div",bo,[t("h2",null,[i(o(Ze),{size:15}),e[98]||(e[98]=W("图片浏览",-1))])]),t("div",fo,[t("label",vo,[e[99]||(e[99]=t("span",null,"幻灯片间隔",-1)),t("div",po,[i(o(ye),{value:g.value.media.imageSlideSeconds,min:2,max:60,step:1,style:{width:"160px"},"onUpdate:value":e[31]||(e[31]=l=>L("imageSlideSeconds",l))},null,8,["value"]),t("b",null,q(g.value.media.imageSlideSeconds)+"s",1)])]),t("label",go,[e[100]||(e[100]=t("span",null,"幻灯片过渡效果",-1)),i(o(ce),{value:g.value.media.imageTransition||"fade",options:[{label:"淡入淡出",value:"fade"},{label:"横向滑动",value:"slide"},{label:"缩放进入",value:"zoom"},{label:"无效果",value:"none"}],"onUpdate:value":e[32]||(e[32]=l=>L("imageTransition",l))},null,8,["value"])]),t("label",mo,[e[101]||(e[101]=t("span",null,"幻灯片循环",-1)),i(o(ne),{value:g.value.media.imageSlideLoop,"onUpdate:value":e[33]||(e[33]=l=>L("imageSlideLoop",l))},null,8,["value"])]),t("label",ho,[e[104]||(e[104]=t("span",null,"幻灯片背景音乐",-1)),t("div",xo,[t("b",{class:"bgm-name",title:Q.value||"未设置"},q(Q.value||"未设置"),9,yo),i(o(le),{size:"tiny",onClick:K},{default:M(()=>[...e[102]||(e[102]=[W("选择",-1)])]),_:1}),Q.value?(u(),H(o(le),{key:0,size:"tiny",quaternary:"",onClick:e[34]||(e[34]=l=>L("imageBackgroundMusic",null))},{default:M(()=>[...e[103]||(e[103]=[W("清除",-1)])]),_:1})):Ae("",!0)])]),t("label",Co,[e[105]||(e[105]=t("span",null,"包含子文件夹",-1)),i(o(ne),{value:g.value.media.recursiveImages,"onUpdate:value":e[35]||(e[35]=l=>L("recursiveImages",l))},null,8,["value"])]),t("label",wo,[e[106]||(e[106]=t("span",null,"漫画模式",-1)),i(o(ne),{value:g.value.media.comicMode,"onUpdate:value":e[36]||(e[36]=l=>L("comicMode",l))},null,8,["value"])]),t("label",So,[e[107]||(e[107]=t("span",null,"漫画宽度",-1)),i(o(ce),{value:g.value.media.comicWidth,options:[{label:"自适应",value:"default"},{label:"全宽",value:"full"},{label:"2/3",value:"2/3"},{label:"1/2",value:"1/2"}],"onUpdate:value":e[37]||(e[37]=l=>L("comicWidth",l))},null,8,["value"])]),t("label",ko,[e[108]||(e[108]=t("span",null,"滑到底自动下一张",-1)),i(o(ne),{value:g.value.media.comicAutoPage,"onUpdate:value":e[38]||(e[38]=l=>L("comicAutoPage",l))},null,8,["value"])])])]),t("section",zo,[t("div",To,[t("h2",null,[i(o(ta),{size:15}),e[109]||(e[109]=W("音乐与视频",-1))])]),t("div",Ro,[t("label",Po,[e[110]||(e[110]=t("span",null,"背景图轮播间隔",-1)),t("div",$o,[i(o(ye),{value:g.value.media.musicBackgroundSeconds,min:.1,max:60,step:.1,style:{width:"160px"},"onUpdate:value":e[39]||(e[39]=l=>L("musicBackgroundSeconds",l))},null,8,["value"]),t("b",null,q(Number(g.value.media.musicBackgroundSeconds).toFixed(1))+"s",1)])]),t("label",Io,[e[111]||(e[111]=t("span",null,"显示歌词面板",-1)),i(o(ne),{value:g.value.media.musicShowLyrics,"onUpdate:value":e[40]||(e[40]=l=>L("musicShowLyrics",l))},null,8,["value"])]),t("label",Bo,[e[112]||(e[112]=t("span",null,"视频自动续播",-1)),i(o(ne),{value:g.value.media.videoAutoResume,"onUpdate:value":e[41]||(e[41]=l=>L("videoAutoResume",l))},null,8,["value"])])])])])]),_:1}),i(o(Ee),{name:"about",tab:"关于"},{default:M(()=>[t("div",Lo,[t("section",Ao,[e[122]||(e[122]=t("div",{class:"about-brand"},[t("div",{class:"brand-mark big"},"☾"),t("div",null,[t("h2",null,"静读天下 · MoonAot"),t("p",null,"WebView2 + NativeAOT 本地阅读器 · SQLite 持久化")])],-1)),t("div",_o,[t("div",null,[e[113]||(e[113]=t("span",null,"程序版本",-1)),t("b",null,q(o(re).appInfo?.version||"-"),1)]),t("div",null,[e[114]||(e[114]=t("span",null,"WebView2",-1)),t("b",null,q(o(re).appInfo?.webViewVersion||"未检测到"),1)]),t("div",null,[e[115]||(e[115]=t("span",null,"运行时",-1)),t("b",null,q(o(re).appInfo?.isAot?"NativeAOT":"JIT / 开发模式"),1)]),t("div",null,[e[116]||(e[116]=t("span",null,"操作系统",-1)),t("b",null,q(o(re).appInfo?.os||"-"),1)]),t("div",null,[e[117]||(e[117]=t("span",null,"藏书数量",-1)),t("b",null,q((o(re).appInfo?.bookCount??0).toLocaleString())+" 本",1)]),t("div",null,[e[118]||(e[118]=t("span",null,"数据目录",-1)),t("b",Eo,q(o(re).appInfo?.dataDir||"-"),1)])]),t("div",Wo,[i(o(le),{onClick:ue},{icon:M(()=>[i(o(pt),{size:15})]),default:M(()=>[e[119]||(e[119]=W("打开数据目录",-1))]),_:1}),i(o(le),{onClick:Se},{icon:M(()=>[i(o(Tt),{size:15})]),default:M(()=>[e[120]||(e[120]=W("打开日志",-1))]),_:1})]),t("div",Mo,[i(o(ra),{size:15}),e[121]||(e[121]=t("p",null,"本程序完全离线运行。阅读器由 readest/foliate-js 驱动，书库、笔记、设置与播放列表使用本地 SQLite 单文件数据库，便于 NativeAOT 单文件发布。",-1))])])])]),_:1})]),_:1},8,["value"])]))}});export{Zo as default};
