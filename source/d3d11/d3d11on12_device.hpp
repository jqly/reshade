/*
 * Copyright (C) 2022 Patrick Mours
 * SPDX-License-Identifier: BSD-3-Clause OR MIT
 */

#pragma once

#include <d3d11.h>
#include <d3d12.h>
#include <d3d11on12.h>
#include "com_ptr.hpp"

struct D3D11Device;
struct D3D12Device;

struct DECLSPEC_UUID("6BE8CF18-2108-4506-AAA0-AD5A29812A31") D3D11On12Device final :
#ifdef __ID3D11On12Device2_INTERFACE_DEFINED__
	ID3D11On12Device2
#else
	ID3D11On12Device1
#endif
{
	D3D11On12Device(D3D11Device *device_11, D3D12Device *device_12, ID3D11On12Device *original);
	~D3D11On12Device();

	D3D11On12Device(const D3D11On12Device &) = delete;
	D3D11On12Device &operator=(const D3D11On12Device &) = delete;

	#pragma region IUnknown
	HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void **ppvObj) override;
	ULONG   STDMETHODCALLTYPE AddRef() override;
	ULONG   STDMETHODCALLTYPE Release() override;
	#pragma endregion
	#pragma region ID3D11On12Device
	HRESULT STDMETHODCALLTYPE CreateWrappedResource(IUnknown *pResource12, const D3D11_RESOURCE_FLAGS *pFlags11, D3D12_RESOURCE_STATES InState, D3D12_RESOURCE_STATES OutState, REFIID riid, void **ppResource11) override;
    void    STDMETHODCALLTYPE ReleaseWrappedResources(ID3D11Resource *const *ppResources, UINT NumResources) override;
    void    STDMETHODCALLTYPE AcquireWrappedResources(ID3D11Resource *const *ppResources, UINT NumResources) override;
	#pragma endregion
	#pragma region ID3D11On12Device1
#ifdef __ID3D11On12Device1_INTERFACE_DEFINED__
	HRESULT STDMETHODCALLTYPE GetD3D12Device(REFIID riid, void **ppvDevice) override;
#endif
	#pragma endregion
	#pragma region ID3D11On12Device2
#ifdef __ID3D11On12Device2_INTERFACE_DEFINED__
	HRESULT STDMETHODCALLTYPE UnwrapUnderlyingResource(ID3D11Resource *pResource11, ID3D12CommandQueue *pCommandQueue, REFIID riid, void **ppvResource12) override;
    HRESULT STDMETHODCALLTYPE ReturnUnderlyingResource(ID3D11Resource *pResource11, UINT NumSync, UINT64 *pSignalValues, ID3D12Fence **ppFences) override;
#endif
	#pragma endregion

	bool check_and_upgrade_interface(REFIID riid);

	ID3D11On12Device *_orig;
	unsigned int _interface_version;
	D3D11Device *const _parent_device_11;
	const com_ptr<D3D12Device> _parent_device_12;
};
