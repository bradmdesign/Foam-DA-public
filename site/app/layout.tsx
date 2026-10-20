import type {Metadata} from 'next';
import {LibraryShell} from '@/components/library-shell';
import './globals.css';
export const metadata:Metadata={title:{default:'Foam DA',template:'%s · Foam DA'},description:'An independent, ad-free reference to the quests, items, paths, and world of Dark Ages.',icons:{icon:'/favicon.svg'}};
const themeScript="try{document.documentElement.dataset.theme=localStorage.getItem('da-library-theme')==='light'?'light':'dark'}catch(e){document.documentElement.dataset.theme='dark'}";
export default function RootLayout({children}:{children:React.ReactNode}){return <html lang="en" data-theme="dark" suppressHydrationWarning><head><script dangerouslySetInnerHTML={{__html:themeScript}}/></head><body><LibraryShell>{children}</LibraryShell></body></html>}
