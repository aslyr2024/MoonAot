"use strict";(self.webpackChunk_N_E=self.webpackChunk_N_E||[]).push([[8740],{28740:(e,t,r)=>{r.d(t,{makeFB2:()=>h});let n=e=>e?e.replace(/[\t\n\f\r ]+/g," ").replace(/^[\t\n\f\r ]+/,"").replace(/[\t\n\f\r ]+$/,""):"",i=e=>n(e?.textContent),o={XLINK:"http://www.w3.org/1999/xlink",EPUB:"http://www.idpf.org/2007/ops"},l={XML:"application/xml",XHTML:"application/xhtml+xml"},a={strong:["strong","self"],emphasis:["em","self"],style:["span","self"],a:"anchor",strikethrough:["s","self"],sub:["sub","self"],sup:["sup","self"],code:["code","self"],image:"image"},s={epigraph:["blockquote"],subtitle:["h2",a],"text-author":["p",a],date:["p",a],stanza:"stanza"},c={title:["header",{p:["h1",a],"empty-line":["br"]}],epigraph:["blockquote","self"],image:"image",annotation:["aside"],section:["section","self"],p:["p",a],poem:["blockquote",s],subtitle:["h2",a],cite:["blockquote","self"],"empty-line":["br"],table:["table",{tr:["tr",{th:["th",a,["colspan","rowspan","align","valign"]],td:["td",a,["colspan","rowspan","align","valign"]]},["align"]]}],"text-author":["p",a]};s.epigraph.push(c);let p={image:"image",title:["section",{p:["h1",a],"empty-line":["br"]}],epigraph:["section",c],section:["section",c]};class d{constructor(e){this.fb2=e,this.doc=document.implementation.createDocument(o.XHTML,"html"),this.bins=new Map(Array.from(this.fb2.getElementsByTagName("binary"),e=>[e.id,e]))}getImageSrc(e){let t=e.getAttributeNS(o.XLINK,"href");if(!t)return"data:,";let[,r]=t.split("#");if(!r)return t;let n=this.bins.get(r);return n?`data:${n.getAttribute("content-type")};base64,${n.textContent}`:t}image(e){let t=this.doc.createElement("img");return t.alt=e.getAttribute("alt"),t.title=e.getAttribute("title"),t.setAttribute("src",this.getImageSrc(e)),t}anchor(e){let t=this.convert(e,{a:["a",a]});return t.setAttribute("href",e.getAttributeNS(o.XLINK,"href")),"note"===e.getAttribute("type")&&t.setAttributeNS(o.EPUB,"epub:type","noteref"),t}stanza(e){let t=this.convert(e,{stanza:["p",{title:["header",{p:["strong",a],"empty-line":["br"]}],subtitle:["p",a]}]});for(let r of e.children)"v"===r.nodeName&&(t.append(this.doc.createTextNode(r.textContent)),t.append(this.doc.createElement("br")));return t}convert(e,t){if(3===e.nodeType)return this.doc.createTextNode(e.textContent);if(4===e.nodeType)return this.doc.createCDATASection(e.textContent);if(8===e.nodeType)return this.doc.createComment(e.textContent);let r=t?.[e.nodeName];if(!r)return null;if("string"==typeof r)return this[r](e);let[n,i,o]=r,l=this.doc.createElement(n);if(e.id&&(l.id=e.id),l.classList.add(e.nodeName),Array.isArray(o))for(let t of o){let r=e.getAttribute(t);r&&l.setAttribute(t,r)}let a="self"===i?t:i,s=e.firstChild;for(;s;){let e=this.convert(s,a);e&&l.append(e),s=s.nextSibling}return l}}let u=async e=>{let t=await e.arrayBuffer(),r=new TextDecoder("utf-8").decode(t),n=new DOMParser,i=n.parseFromString(r,l.XML),o=i.xmlEncoding||r.match(/^<\?xml\s+version\s*=\s*["']1.\d+"\s+encoding\s*=\s*["']([A-Za-z0-9._-]*)["']/)?.[1];if(o&&"utf-8"!==o.toLowerCase()){let e=new TextDecoder(o).decode(t);return n.parseFromString(e,l.XML)}return i},m=URL.createObjectURL(new Blob([`
@namespace epub "http://www.idpf.org/2007/ops";
body > img, section > img {
    display: block;
    margin: auto;
}
.title h1 {
    text-align: center;
}
body > section > .title, body.notesBodyType > .title {
    margin: 3em 0;
}
body.notesBodyType > section .title h1 {
    text-align: start;
}
body.notesBodyType > section .title {
    margin: 1em 0;
}
p {
    text-indent: 1em;
    margin: 0;
}
:not(p) + p, p:first-child {
    text-indent: 0;
}
.poem p {
    text-indent: 0;
    margin: 1em 0;
}
.text-author, .date {
    text-align: end;
}
.text-author:before {
    content: "—";
}
table {
    border-collapse: collapse;
}
td, th {
    padding: .25em;
}
a[epub|type~="noteref"] {
    font-size: .75em;
    vertical-align: super;
}
body:not(.notesBodyType) > .title, body:not(.notesBodyType) > .epigraph {
    margin: 3em 0;
}
`],{type:"text/css"})),f=e=>`<?xml version="1.0" encoding="utf-8"?>
<html xmlns="http://www.w3.org/1999/xhtml">
    <head><link href="${m}" rel="stylesheet" type="text/css"/></head>
    <body>${e}</body>
</html>`,g="data-foliate-id",h=async e=>{let t={},r=await u(e),o=new d(r),a=e=>r.querySelector(e),s=e=>[...r.querySelectorAll(e)],m=e=>{let t=i(e.querySelector("nickname"));if(t)return t;let r=i(e.querySelector("first-name")),n=i(e.querySelector("middle-name")),o=i(e.querySelector("last-name"));return{name:[r,n,o].filter(e=>e).join(" "),sortAs:o?[o,[r,n].filter(e=>e).join(" ")].join(", "):null}},h=e=>e?.getAttribute("value")??i(e),b=a("title-info annotation");if(t.metadata={title:i(a("title-info book-title")),identifier:i(a("document-info id")),language:i(a("title-info lang")),author:s("title-info author").map(m),translator:s("title-info translator").map(m),contributor:s("document-info author").map(m).concat(s("document-info program-used").map(i)).map(e=>Object.assign("string"==typeof e?{name:e}:e,{role:"bkp"})),publisher:i(a("publish-info publisher")),published:h(a("title-info date")),modified:h(a("document-info date")),description:b?o.convert(b,{annotation:["div",c]}).innerHTML:null,subject:s("title-info genre").map(i)},a("coverpage image")){let e=o.getImageSrc(a("coverpage image"));t.getCover=()=>fetch(e).then(e=>e.blob())}else t.getCover=()=>null;let y=Array.from(r.querySelectorAll("body"),e=>{let t=o.convert(e,{body:["body",p]});return[Array.from(t.children,e=>{let t=[e,...e.querySelectorAll("[id]")].map(e=>e.id);return{el:e,ids:t}}),t]}),x=[],w=y[0][0].map(({el:e,ids:t})=>({ids:t,titles:Array.from(e.querySelectorAll(":scope > section > .title"),(e,t)=>(e.setAttribute(g,t),{title:i(e),index:t})),el:e})).concat(y.slice(1).map(([e,t])=>{let r=e.map(e=>e.ids).flat();return t.classList.add("notesBodyType"),{ids:r,el:t,linear:"no"}})).map(({ids:e,titles:t,el:r,linear:i})=>{let o=f(r.outerHTML),a=new Blob([o],{type:l.XHTML}),s=URL.createObjectURL(a);return x.push(s),{ids:e,title:n(r.querySelector(".title, .subtitle, p")?.textContent??(r.classList.contains("title")?r.textContent:"")),titles:t,load:()=>s,createDocument:()=>new DOMParser().parseFromString(o,l.XHTML),size:a.size-Array.from(r.querySelectorAll("[src]"),e=>e.getAttribute("src")?.length??0).reduce((e,t)=>e+t,0),linear:i}}),A=new Map;return t.sections=w.map((e,t)=>{let{ids:r,load:n,createDocument:i,size:o,linear:l}=e;for(let e of r)e&&A.set(e,t);return{id:t,load:n,createDocument:i,size:o,linear:l}}),t.toc=w.map(({title:e,titles:t},r)=>{let n=r.toString();return{label:e,href:n,subitems:t?.length?t.map(({title:e,index:t})=>({label:e,href:`${n}#${t}`})):null}}).filter(e=>e),t.resolveHref=e=>{let[t,r]=e.split("#");return t?{index:Number(t),anchor:e=>e.querySelector(`[${g}="${r}"]`)}:{index:A.get(r),anchor:e=>e.getElementById(r)}},t.splitTOCHref=e=>e?.split("#")?.map(e=>Number(e))??[],t.getTOCFragment=(e,t)=>e.querySelector(`[${g}="${t}"]`),t.destroy=()=>{for(let e of x)URL.revokeObjectURL(e)},t}}}]);