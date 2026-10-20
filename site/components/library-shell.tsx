'use client';
import {useEffect,useState} from 'react';
import {usePathname} from 'next/navigation';
import {BookOpen,Search,Sun} from 'lucide-react';
import {Sidebar,SidebarProvider,SidebarTrigger} from '@/components/ui/sidebar';
import {Switch} from '@/components/ui/switch';
import {LibraryIcon} from './library-icon';
import {navigationInfo as sectionInfo} from '@/lib/sections';
export function LibraryShell({children}:{children:React.ReactNode}){
 const pathname=usePathname();const [light,setLight]=useState(false);
 useEffect(()=>{setLight(document.documentElement.dataset.theme==='light');const key=(e:KeyboardEvent)=>{if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='k'){e.preventDefault();window.location.href='/search/';}};window.addEventListener('keydown',key);return()=>window.removeEventListener('keydown',key)},[]);
 function theme(value:boolean){setLight(value);document.documentElement.dataset.theme=value?'light':'dark';try{localStorage.setItem('da-library-theme',value?'light':'dark')}catch{}}
 return <SidebarProvider className="library-shell" style={{'--sidebar-width':'14.5rem'} as React.CSSProperties}>
  <a className="skip-link" href="#main">Skip to content</a>
  <header className="topbar" data-pagefind-ignore><SidebarTrigger className="menu-toggle" aria-label="Open navigation"/><a className="brand" href="/"><BookOpen className="brand-mark" strokeWidth={1.3}/><span className="brand-name">Foam DA<small>Library</small></span></a><a className="header-search" href="/search/"><Search size={17}/><span>Search the library</span><kbd>Ctrl K</kbd></a><div className="theme-control"><Sun size={17} aria-hidden="true"/><label htmlFor="theme-switch">Light mode</label><Switch id="theme-switch" checked={light} onCheckedChange={theme}/></div></header>
  <Sidebar className="library-sidebar"><div data-pagefind-ignore><div className="side-label">The library</div><nav className="nav-list" aria-label="Main navigation">{sectionInfo.map(s=><a className="nav-item" key={s.id} href={'/'+s.id+'/'} aria-current={(pathname==='/'+s.id||pathname?.startsWith('/'+s.id+'/'))?'page':undefined}><LibraryIcon name={s.icon}/>{s.title}</a>)}</nav><div className="side-footer"><a href="/about/">About the Library</a><span>A reference for Aislings.<br/>Rooted in Temuair.</span></div></div></Sidebar>
  <div className="page-surface">{children}</div></SidebarProvider>
}
