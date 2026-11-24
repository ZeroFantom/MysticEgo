import React from 'react';
import {createRoot} from 'react-dom/client';
import {MysticApp} from '@mysticego/ui';
import type {NodeMode} from '@mysticego/sdk';
import '@mysticego/ui/styles.css';

const mode = document.documentElement.dataset.role as NodeMode;
const ports = {orchestra:5500,global:5100,local:5200,agent:5300,audit:5350};
createRoot(document.getElementById('root')!).render(
  <React.StrictMode><MysticApp mode={mode} title={document.title} defaultApi={'http://127.0.0.1:'+ports[mode]}/></React.StrictMode>
);
