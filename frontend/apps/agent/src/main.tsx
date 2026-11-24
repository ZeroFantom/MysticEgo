import React from 'react';
import {createRoot} from 'react-dom/client';
import {MysticApp} from '@mysticego/ui';
import '@mysticego/ui/styles.css';
createRoot(document.getElementById('root')!).render(<React.StrictMode><MysticApp mode="agent" title="Панель компьютера" defaultApi="http://localhost:5300"/></React.StrictMode>);
